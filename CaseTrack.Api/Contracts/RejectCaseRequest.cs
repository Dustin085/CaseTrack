using System.ComponentModel.DataAnnotations;

namespace CaseTrack.Api.Contracts;

// 長度跟資料庫的 RejectionReason（nvarchar(500)）一致，超過時回 400 而不是資料庫錯誤
public sealed record RejectCaseRequest(
    [Required(ErrorMessage = "退件原因不可為空")]
    [MaxLength(500, ErrorMessage = "退件原因長度不能超過 500 個字")]
    string Reason);
