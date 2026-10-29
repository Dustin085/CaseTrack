using System.ComponentModel.DataAnnotations;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Api.Contracts;

// 報表的查詢條件（查詢字串）：?from=2026-10-01&to=2026-10-31&status=Closed。
// 日期是台灣的日期、含頭含尾；用 DateOnly? + [Required]：沒給時回 400，不會變成 0001-01-01
public sealed record ReportRequest(
    [Required(ErrorMessage = "請選擇開始日期")]
    DateOnly? From,

    [Required(ErrorMessage = "請選擇結束日期")]
    DateOnly? To,

    // 只有案件清單使用
    CaseStatus? Status = null);
