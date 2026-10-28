namespace CaseTrack.Application.Cases;

// 上傳的檔案。IFormFile 是 ASP.NET Core 的型別，不能進 Application（就像 Java 的 Service 不該依賴 MultipartFile）。
// FileName 是用戶端送來的值，只能用來顯示；Size 是伺服器實際收到的大小；類型只能由 Content 的內容判斷。
// Content 必須可以 Seek（先檢查檔頭再存檔，要能回到開頭），由呼叫端負責 Dispose（實際上是框架在請求結束時）
public sealed record UploadedFile(string FileName, long Size, Stream Content);
