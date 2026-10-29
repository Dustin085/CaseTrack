using CaseTrack.Api.Authentication;
using CaseTrack.Api.Contracts;
using CaseTrack.Api.Reports;
using CaseTrack.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.Controllers;

// 報表匯出（Excel）。看得到案件的人才能匯出（CaseReader），內容也只有自己看得到的案件（CaseAccessScope）。
// no-store：報表含案件資料，不能留在瀏覽器或 proxy 的快取裡
[Authorize(Policy = Policies.CaseReader)]
[ApiController]
[Route("api/reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReportsController : ControllerBase
{
    // GET api/reports/statistics?from=2026-10-01&to=2026-10-31
    [HttpGet("statistics")]
    public async Task<FileContentResult> Statistics(
        [FromQuery] ReportRequest request,
        [FromServices] CaseReportHandler handler,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var (from, to) = (request.From!.Value, request.To!.Value);
        var counts = await handler.GetStatisticsAsync(from, to, cancellationToken);
        var content = CaseReportWorkbook.Statistics(counts, from, to, timeProvider.GetUtcNow());
        return ExcelFile(content, $"案件統計_{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
    }

    // GET api/reports/case-list?from=2026-10-01&to=2026-10-31&status=Closed
    [HttpGet("case-list")]
    public async Task<FileContentResult> CaseList(
        [FromQuery] ReportRequest request,
        [FromServices] CaseReportHandler handler,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var (from, to) = (request.From!.Value, request.To!.Value);
        var rows = await handler.GetCaseListAsync(from, to, request.Status, cancellationToken);
        var content = CaseReportWorkbook.CaseList(rows, from, to, timeProvider.GetUtcNow());
        return ExcelFile(content, $"案件清單_{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
    }

    // 跟附件下載一樣：Content-Disposition: attachment（中文檔名自動加上 filename*）、nosniff
    private FileContentResult ExcelFile(byte[] content, string fileName)
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content, CaseReportWorkbook.ContentType, fileName);
    }
}
