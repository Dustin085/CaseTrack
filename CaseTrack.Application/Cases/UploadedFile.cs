namespace CaseTrack.Application.Cases;

// 上傳的檔案。IFormFile 是 ASP.NET Core 的型別，不能進 Application（就像 Java 的 Service 不該依賴 MultipartFile）。
// FileName、Size 都是用戶端送來的值，只有 Content 的內容能拿來判斷類型。
// Content 必須可以 Seek（先檢查檔頭再存檔，要能回到開頭），由呼叫端負責 Dispose（實際上是框架在請求結束時）
public sealed record UploadedFile(string FileName, long Size, Stream Content);
