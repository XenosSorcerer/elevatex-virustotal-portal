using ClosedXML.Excel;
using ElevateX.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace ElevateX.Core.Services;

public interface IExportService
{
    Task<byte[]> BuildSubmissionsXlsxAsync(CancellationToken ct = default);
}

/// <summary>FR-12 stand-out: flat .xlsx of every submission plus its scan outcome.</summary>
public sealed class ExportService : IExportService
{
    private readonly AppDbContext _db;

    public ExportService(AppDbContext db) => _db = db;

    public async Task<byte[]> BuildSubmissionsXlsxAsync(CancellationToken ct = default)
    {
        var rows = await _db.Submissions
            .AsNoTracking()
            .Include(s => s.FileAnalysis)
            .OrderByDescending(s => s.SubmittedAtUtc)
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Submissions");

        string[] headers =
        {
            "Submitted (UTC)", "File Name", "SHA-256", "Source", "Priority", "Target Dept/System",
            "Reason for Suspicion", "Status", "Malicious", "Suspicious", "Total Engines",
            "Scan Summary", "VirusTotal Report", "Failure Reason"
        };

        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);

        var r = 2;
        foreach (var s in rows)
        {
            var fa = s.FileAnalysis;

            ws.Cell(r, 1).Value = s.SubmittedAtUtc;
            ws.Cell(r, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
            ws.Cell(r, 2).Value = fa?.FileName ?? string.Empty;
            ws.Cell(r, 3).Value = fa?.Sha256 ?? string.Empty;
            ws.Cell(r, 4).Value = s.Source.ToString();
            ws.Cell(r, 5).Value = s.ThreatPriority.ToString();
            ws.Cell(r, 6).Value = s.TargetDepartment ?? string.Empty;
            ws.Cell(r, 7).Value = s.ReasonForSuspicion;
            ws.Cell(r, 8).Value = fa?.Status.ToString() ?? string.Empty;
            ws.Cell(r, 9).Value = fa?.MaliciousCount ?? 0;
            ws.Cell(r, 10).Value = fa?.SuspiciousCount ?? 0;
            ws.Cell(r, 11).Value = fa?.TotalEngines ?? 0;
            ws.Cell(r, 12).Value = fa?.ScanSummary ?? string.Empty;
            ws.Cell(r, 13).Value = fa?.VirusTotalReportUrl ?? string.Empty;
            ws.Cell(r, 14).Value = fa?.FailureReason ?? string.Empty;
            r++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
