using System.ComponentModel.DataAnnotations;

namespace CaseTrack.Api.Contracts;

// 不予受理的原因跟退件原因存在同一個欄位（nvarchar(500)）
public sealed record DismissCaseRequest(
    [Required(ErrorMessage = "不予受理的原因不可為空")]
    [MaxLength(500, ErrorMessage = "不予受理的原因長度不能超過 500 個字")]
    string Reason);
