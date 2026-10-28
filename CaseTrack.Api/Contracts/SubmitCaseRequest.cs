using System.ComponentModel.DataAnnotations;

namespace CaseTrack.Api.Contracts;

// API 的輸入格式與驗證（Web 層的關注點），不放在 Application。
// record 的驗證標註要放在主建構子參數上，不能加 property:，否則 ASP.NET Core 會在執行時丟例外。
public sealed record SubmitCaseRequest(
    [Required(ErrorMessage = "主旨不可為空")]
    [MaxLength(200, ErrorMessage = "主旨長度不能超過 200 個字")]
    string Subject,

    [Required(ErrorMessage = "內容不可為空")]
    string Content,

    [Required(ErrorMessage = "陳情人資料不可為空")]
    PetitionerRequest Petitioner,

    // multipart/form-data 裡同一個欄位名稱（Attachments）出現好幾個部分，就會綁定成清單。
    // 沒有附件時是 null，由 ToUploadedFiles 當成空清單處理
    List<IFormFile>? Attachments);

// 「電話與 Email 至少一項」跨兩個欄位，單一欄位的標註表達不了，由 Domain 的 Petitioner 檢查
public sealed record PetitionerRequest(
    [Required(ErrorMessage = "陳情人姓名不可為空")]
    [MaxLength(50, ErrorMessage = "陳情人姓名長度不能超過 50 個字")]
    string Name,

    [MaxLength(20, ErrorMessage = "電話長度不能超過 20 個字")]
    string? Phone,

    [MaxLength(254, ErrorMessage = "Email 長度不能超過 254 個字")]
    string? Email,

    [MaxLength(200, ErrorMessage = "地址長度不能超過 200 個字")]
    string? Address);
