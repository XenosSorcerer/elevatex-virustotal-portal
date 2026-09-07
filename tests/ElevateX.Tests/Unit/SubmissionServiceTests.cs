using System.Text;
using ElevateX.Core.Data;
using ElevateX.Core.Entities;
using ElevateX.Core.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ElevateX.Tests.Unit;

public class SubmissionServiceTests : IDisposable
{
    private static readonly byte[] Sample = Encoding.UTF8.GetBytes("suspicious payload from a phishing email");

    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly SubmissionService _svc;

    public SubmissionServiceTests()
    {
        _db = TestSupport.NewDb(out _conn);
        _svc = new SubmissionService(_db, NullLogger<SubmissionService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private Task<SubmissionResult> Submit(
        SubmissionSource source = SubmissionSource.EmailAttachment, string reason = "macro-enabled doc from a spoofed sender") =>
        _svc.SubmitFileAsync(new MemoryStream(Sample), "invoice.docm", Sample.Length, source, reason);

    [Fact]
    public async Task A_new_file_is_persisted_as_a_queued_analysis()
    {
        var result = await Submit();

        result.RequiresScanning.Should().BeTrue();
        result.IsDuplicate.Should().BeFalse();
        (await _db.FileAnalyses.CountAsync()).Should().Be(1);
        (await _db.Submissions.CountAsync()).Should().Be(1);
        (await _db.FileAnalyses.SingleAsync()).Status.Should().Be(AnalysisStatus.Queued);
    }

    [Fact]
    public async Task Resubmitting_a_completed_file_links_to_it_without_rescanning()
    {
        await Submit();
        var existing = await _db.FileAnalyses.SingleAsync();
        existing.Status = AnalysisStatus.Completed;
        existing.CurrentStage = AnalysisStage.Done;
        await _db.SaveChangesAsync();

        var result = await Submit(SubmissionSource.UsbDrive, "same sample, different mailbox");

        result.RequiresScanning.Should().BeFalse();
        result.IsDuplicate.Should().BeTrue();
        result.FileAnalysis.Id.Should().Be(existing.Id);
        (await _db.FileAnalyses.CountAsync()).Should().Be(1);
        (await _db.Submissions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Resubmitting_while_in_flight_attaches_to_the_existing_job()
    {
        await Submit(); // stays Queued

        var result = await Submit(SubmissionSource.WebDownload);

        result.RequiresScanning.Should().BeFalse();
        (await _db.FileAnalyses.CountAsync()).Should().Be(1);
        (await _db.Submissions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Resubmitting_a_failed_file_re_queues_it_and_clears_the_failure()
    {
        await Submit();
        var existing = await _db.FileAnalyses.SingleAsync();
        existing.Status = AnalysisStatus.Failed;
        existing.CurrentStage = AnalysisStage.Failed;
        existing.FailureReason = "429 wall after bounded retries";
        existing.RetryCount = 3;
        await _db.SaveChangesAsync();

        var result = await Submit();

        result.RequiresScanning.Should().BeTrue();
        var reloaded = await _db.FileAnalyses.SingleAsync();
        reloaded.Status.Should().Be(AnalysisStatus.Queued);
        reloaded.FailureReason.Should().BeNull();
        reloaded.RetryCount.Should().Be(0);
    }
}
