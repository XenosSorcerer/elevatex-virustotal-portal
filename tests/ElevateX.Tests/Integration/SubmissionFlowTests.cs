using System.Text;
using ElevateX.Core.Data;
using ElevateX.Core.Entities;
using ElevateX.Core.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElevateX.Tests.Integration;

/// <summary>
/// VT-04: one end-to-end proof that a submission flows upload -> (faked) pipeline -> Completed,
/// and that an identical resubmission is served from the existing analysis with no extra quota.
/// The real background dispatcher runs; only the VirusTotal client and the database are swapped.
/// </summary>
public class SubmissionFlowTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"elevatex_it_{Guid.NewGuid():N}.db");
    private readonly FakeVirusTotalClient _fake = new();
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                var dbOptions = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (dbOptions is not null) services.Remove(dbOptions);
                services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));

                services.RemoveAll<IVirusTotalClient>();
                services.AddSingleton<IVirusTotalClient>(_fake);
            });
        });

        _ = _factory.Services; // build + start the host: DB provisions, dispatcher starts
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(file); } catch { /* best effort */ }
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_submission_flows_from_upload_through_the_pipeline_to_completed()
    {
        var bytes = Encoding.UTF8.GetBytes($"integration sample {Guid.NewGuid()}");

        Guid analysisId;
        using (var scope = _factory.Services.CreateScope())
        {
            var submissions = scope.ServiceProvider.GetRequiredService<ISubmissionService>();
            var result = await submissions.SubmitFileAsync(
                new MemoryStream(bytes), "int.bin", bytes.Length,
                SubmissionSource.EmailAttachment, "end to end");

            result.RequiresScanning.Should().BeTrue();
            analysisId = result.FileAnalysis.Id;
        }

        var status = await WaitForAsync(analysisId, s => s is AnalysisStatus.Completed or AnalysisStatus.Failed);
        status.Should().Be(AnalysisStatus.Completed);
    }

    [Fact]
    public async Task Resubmitting_identical_bytes_reuses_the_analysis_and_spends_no_extra_quota()
    {
        var bytes = Encoding.UTF8.GetBytes($"dedupe sample {Guid.NewGuid()}");

        Guid firstId;
        using (var scope = _factory.Services.CreateScope())
        {
            var submissions = scope.ServiceProvider.GetRequiredService<ISubmissionService>();
            firstId = (await submissions.SubmitFileAsync(new MemoryStream(bytes), "d1.bin", bytes.Length,
                SubmissionSource.UsbDrive, "first")).FileAnalysis.Id;
        }
        await WaitForAsync(firstId, s => s == AnalysisStatus.Completed);
        var callsAfterFirstScan = TotalFakeCalls();

        using (var scope = _factory.Services.CreateScope())
        {
            var submissions = scope.ServiceProvider.GetRequiredService<ISubmissionService>();
            var second = await submissions.SubmitFileAsync(new MemoryStream(bytes), "d2.bin", bytes.Length,
                SubmissionSource.WebDownload, "same file, another mailbox");

            second.IsDuplicate.Should().BeTrue();
            second.RequiresScanning.Should().BeFalse();
        }

        await Task.Delay(500); // give the dispatcher several idle polls to (not) act
        TotalFakeCalls().Should().Be(callsAfterFirstScan);

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.FileAnalyses.CountAsync()).Should().Be(1);
        (await db.Submissions.CountAsync()).Should().Be(2);
    }

    private int TotalFakeCalls() =>
        _fake.GetFileReportCallCount + _fake.UploadFileCallCount + _fake.GetAnalysisStatusCallCount;

    private async Task<AnalysisStatus> WaitForAsync(Guid id, Func<AnalysisStatus, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var status = await db.FileAnalyses.Where(a => a.Id == id).Select(a => a.Status).FirstOrDefaultAsync();
                if (predicate(status)) return status;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Analysis {id} did not reach the expected status within the deadline.");
    }
}
