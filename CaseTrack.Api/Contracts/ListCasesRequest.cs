using System.ComponentModel.DataAnnotations;
using CaseTrack.Application.Cases;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Api.Contracts;

// GET /api/cases 的查詢字串。參數都有預設值：什麼都不帶就是第 1 頁、每頁 20 筆。
// ErrorMessage 用 {1}、{2} 引用標註本身的數字（Range 是最小值、最大值；MaxLength 是長度上限），
// 上限只定義在 CaseListQuery，改了上限訊息會自動跟著變
public sealed record ListCasesRequest(
    CaseStatus? Status = null,

    [MaxLength(100, ErrorMessage = "關鍵字長度不能超過 {1} 個字元")]
    string? Keyword = null,

    [Range(1, int.MaxValue, ErrorMessage = "頁碼必須大於或等於 {1}")]
    int Page = 1,

    [Range(1, CaseListQuery.MaxPageSize, ErrorMessage = "每頁筆數必須介於 {1} 到 {2} 之間")]
    int PageSize = CaseListQuery.DefaultPageSize);
