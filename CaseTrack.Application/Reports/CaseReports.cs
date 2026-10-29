using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Reports;

// 報表：統計表（單位 × 狀態的件數）與案件清單。期間以送件時間（台灣的日期）為準。
// 看得到哪些案件跟列表一樣由 CaseAccessScope 決定：承辦人、主管只有本單位，分派人員全部
public sealed record DepartmentStatusCount(Guid? DepartmentId, string? DepartmentName, CaseStatus Status, int Count);

// 案件清單的一列。刻意不含陳情人：匯出的檔案會離開系統（寄出、存在個人電腦），
// 依個資最小化原則不放個資；需要聯絡民眾時回到系統查看（會留下查詢紀錄）
public sealed record CaseListRow(
    string CaseNumber,
    string Subject,
    CaseStatus Status,
    string? DepartmentName,
    DateTimeOffset SubmittedAt);

// 讀取專用的查詢（JOIN 單位名稱、GROUP BY），不經過 Repository：Repository 是給聚合讀寫用的
public interface ICaseReportQuery
{
    Task<IReadOnlyList<DepartmentStatusCount>> CountByDepartmentAndStatusAsync(
        DateTimeOffset from, DateTimeOffset to, CaseAccessScope scope, CancellationToken cancellationToken);

    // 最多回傳 limit 筆，依送件時間排序
    Task<IReadOnlyList<CaseListRow>> ListAsync(
        DateTimeOffset from, DateTimeOffset to, CaseStatus? status, CaseAccessScope scope, int limit,
        CancellationToken cancellationToken);
}

public sealed class CaseReportHandler
{
    // 一次最多匯出的筆數：整份檔案在記憶體裡產生，太大會拖垮伺服器。超過就請使用者縮小期間
    public const int MaxListRows = 10_000;
    // 期間最長一年：統計是 GROUP BY，不受筆數限制，但期間太長的報表通常是誤操作
    public const int MaxPeriodDays = 366;

    private static readonly TimeSpan TaiwanOffset = TimeSpan.FromHours(8);

    private readonly ICaseReportQuery _query;
    private readonly ICurrentUser _currentUser;

    public CaseReportHandler(ICaseReportQuery query, ICurrentUser currentUser)
    {
        _query = query;
        _currentUser = currentUser;
    }

    public Task<IReadOnlyList<DepartmentStatusCount>> GetStatisticsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var (start, end) = ToRange(from, to);
        return _query.CountByDepartmentAndStatusAsync(start, end, CaseAccessScope.For(_currentUser), cancellationToken);
    }

    public async Task<IReadOnlyList<CaseListRow>> GetCaseListAsync(
        DateOnly from, DateOnly to, CaseStatus? status, CancellationToken cancellationToken)
    {
        var (start, end) = ToRange(from, to);
        // 多查一筆：查得到第 MaxListRows + 1 筆，代表超過上限。不先 COUNT，少一次查詢
        var rows = await _query.ListAsync(
            start, end, status, CaseAccessScope.For(_currentUser), MaxListRows + 1, cancellationToken);
        if (rows.Count > MaxListRows)
        {
            throw new RequestValidationException("To", $"資料超過 {MaxListRows} 筆，請縮小期間或加上狀態篩選");
        }
        return rows;
    }

    // 日期是台灣的日期，含頭含尾：10/1～10/31 = [10/1 00:00+08:00, 11/1 00:00+08:00)。
    // 結束用「隔天 0 點、不含」：寫成 10/31 23:59:59 會漏掉最後一秒內的資料
    private static (DateTimeOffset Start, DateTimeOffset End) ToRange(DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            throw new RequestValidationException("From", "開始日期不能晚於結束日期");
        }
        if (to.DayNumber - from.DayNumber + 1 > MaxPeriodDays)
        {
            throw new RequestValidationException("To", $"期間不能超過 {MaxPeriodDays} 天");
        }

        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TaiwanOffset);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TaiwanOffset);
        return (start, end);
    }
}
