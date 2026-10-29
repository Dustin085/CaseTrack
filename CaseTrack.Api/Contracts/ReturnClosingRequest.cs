using System.ComponentModel.DataAnnotations;

namespace CaseTrack.Api.Contracts;

// 長度跟資料庫的 ClosingReturnReason（nvarchar(500)）一致
public sealed record ReturnClosingRequest(
    [Required(ErrorMessage = "退回原因不可為空")]
    [MaxLength(500, ErrorMessage = "退回原因長度不能超過 500 個字")]
    string Reason);
