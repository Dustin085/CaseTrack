using System.ComponentModel.DataAnnotations;

namespace CaseTrack.Api.Contracts;

// 長度跟資料庫的 SupplementRequests.Reason（nvarchar(500)）一致
public sealed record RequestSupplementRequest(
    [Required(ErrorMessage = "補件原因不可為空")]
    [MaxLength(500, ErrorMessage = "補件原因長度不能超過 500 個字")]
    string Reason);
