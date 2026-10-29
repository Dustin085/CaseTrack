using System.ComponentModel.DataAnnotations;
using CaseTrack.Application.Auditing;

namespace CaseTrack.Api.Contracts;

// GET /api/admin/audit-entries 的查詢字串，條件都可以不帶
public sealed record ListAuditEntriesRequest(
    [MaxLength(100, ErrorMessage = "資料類型長度不能超過 {1} 個字元")]
    string? EntityType = null,

    [MaxLength(50, ErrorMessage = "資料 Id 長度不能超過 {1} 個字元")]
    string? EntityId = null,

    [MaxLength(100, ErrorMessage = "異動者長度不能超過 {1} 個字元")]
    string? ChangedBy = null,

    DateOnly? From = null,
    DateOnly? To = null,

    [Range(1, int.MaxValue, ErrorMessage = "頁碼必須大於或等於 {1}")]
    int Page = 1,

    [Range(1, AuditLogQuery.MaxPageSize, ErrorMessage = "每頁筆數必須介於 {1} 到 {2} 之間")]
    int PageSize = AuditLogQuery.DefaultPageSize);
