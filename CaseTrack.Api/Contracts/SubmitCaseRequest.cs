using System.ComponentModel.DataAnnotations;

namespace CaseTrack.Api.Contracts;

// API 的輸入格式與驗證（Web 層的關注點），不放在 Application。
// record 的驗證標註要放在主建構子參數上，不能加 property:，否則 ASP.NET Core 會在執行時丟例外。
public sealed record SubmitCaseRequest(
    [Required(ErrorMessage = "主旨不可為空")]
    [MaxLength(200, ErrorMessage = "主旨長度不能超過 200 個字")]
    string Subject,

    [Required(ErrorMessage = "內容不可為空")]
    string Content);
