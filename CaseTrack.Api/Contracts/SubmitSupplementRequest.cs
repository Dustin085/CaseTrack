using System.ComponentModel.DataAnnotations;

namespace CaseTrack.Api.Contracts;

// 長度跟資料庫的 SubmittedContent（nvarchar(2000)）一致
public sealed record SubmitSupplementRequest(
    [Required(ErrorMessage = "補件說明不能為空")]
    [MaxLength(2000, ErrorMessage = "補件說明長度不能超過 2000 個字元")]
    string Content,

    // multipart/form-data 裡同一個欄位名稱（Attachments）出現好幾個部分，就會綁定成清單。
    // 沒有附件時是 null，由 ToUploadedFiles 當成空清單處理
    List<IFormFile>? Attachments);
