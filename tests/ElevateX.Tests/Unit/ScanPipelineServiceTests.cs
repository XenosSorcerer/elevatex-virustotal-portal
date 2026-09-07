using ElevateX.Core.Data;
using ElevateX.Core.Entities;
using ElevateX.Core.Models;
using ElevateX.Core.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ElevateX.Tests.Unit;

public class ScanPipelineServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;

    public ScanPipelineServiceTests() => _db = TestSupport.NewDb(out _conn);

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private ScanPipelineService Pipeline(IVirusTotalClient vt, Action<VirusTotalOptions>? cfg = null) =>
        new(_db, vt, NullLogger<ScanPipelineService>.Instance, TestSupport.Opt(cfg));

    private async Task<FileAnalysis> SeedQueuedAsync(string sha, long size = 100)
    {
        var fa = new FileAnalysis
        {
            Sha256 = sha,
            FileName = "sample.bin",
            FileSizeBytes = size,
            Status = AnalysisStatus.Queued,
            CurrentStage = AnalysisStage.Queued
        };
        _db.FileAnalyses.Add(fa);
        await _db.SaveChangesAsync();
        return fa;
    }

    private static void WriteUploadBlob(string sha)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "uploads");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, sha), new byte[] { 1, 2, 3, 4 });
    }

    [Fact]
    public async Task Known_hash_completes_in_a_single_lookup_without_uploading()
    {
        var fake = new FakeVirusTotalClient();
        fake.SeedFile("known-hash", malicious: 5, suspicious: 1, undetected: 60, harmless: 6);
        var fa = await SeedQueuedAsync("known-hash");

        await Pipeline(fake).ProcessScanAsync(fa.Id);

        var done = await _db.FileAnalyses.FindAsync(fa.Id);
        done!.Status.Should().Be(AnalysisStatus.Completed);
        done.CurrentStage.Should().Be(AnalysisStage.Done);
        done.MaliciousCount.Should().Be(5);
        done.TotalEngines.Should().Be(72);
        fake.GetFileReportCallCount.Should().Be(1);
        fake.UploadFileCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_hash_uploads_then_polls_to_completion()
    {
        var fake = new FakeVirusTotalClient(); // nothing seeded -> lookup returns null
        var fa = await SeedQueuedAsync("unknown-hash");
        WriteUploadBlob("unknown-hash");

        await Pipeline(fake).ProcessScanAsync(fa.Id);

        var done = await _db.FileAnalyses.FindAsync(fa.Id);
        done!.Status.Should().Be(AnalysisStatus.Completed);
        fake.GetFileReportCallCount.Should().Be(1);
        fake.UploadFileCallCount.Should().Be(1);
        fake.GetAnalysisStatusCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Files_over_the_32_mb_limit_complete_without_an_upload()
    {
        var fake = new FakeVirusTotalClient();
        var fa = await SeedQueuedAsync("big-hash", size: 40L * 1024 * 1024);

        await Pipeline(fake).ProcessScanAsync(fa.Id);

        var done = await _db.FileAnalyses.FindAsync(fa.Id);
        done!.Status.Should().Be(AnalysisStatus.Completed);
        done.ScanSummary.Should().Contain("32 MB");
        fake.UploadFileCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Analysis_that_never_completes_fails_after_the_poll_budget()
    {
        var vt = Substitute.For<IVirusTotalClient>();
        vt.GetFileReportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((VirusTotalFileReport?)null);
        vt.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("analysis-1");
        vt.GetAnalysisStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new VirusTotalAnalysisReport { Id = "analysis-1", Status = "in-progress" });

        var fa = await SeedQueuedAsync("slow-hash");
        WriteUploadBlob("slow-hash");
        var pipeline = Pipeline(vt, o => o.MaxPollAttempts = 2);

        for (var pass = 0; pass < 6; pass++)
        {
            var current = await _db.FileAnalyses.AsNoTracking().FirstAsync(a => a.Id == fa.Id);
            if (current.Status == AnalysisStatus.Failed) break;
            await pipeline.ProcessScanAsync(fa.Id);
        }

        var done = await _db.FileAnalyses.AsNoTracking().FirstAsync(a => a.Id == fa.Id);
        done.Status.Should().Be(AnalysisStatus.Failed);
        done.FailureReason.Should().Contain("did not complete");
        done.PollCount.Should().BeGreaterThan(2);
    }
}
