using CaseTrack.Application.Cases;

namespace CaseTrack.Api.Common;

// 把 ASP.NET Core 的 IFormFile 轉成 Application 的 UploadedFile（IFormFile 不進 Application）
public static class UploadedFileMapping
{
    // Length 是實際收到的大小。Stream 不需要 using：上傳的內容由框架暫存（小的在記憶體、大的寫成暫存檔），
    // 請求結束時自動清理；OpenReadStream 只是讀那份暫存資料，而且可以 Seek（先檢查檔頭再存檔需要）
    public static UploadedFile ToUploadedFile(this IFormFile file)
    {
        return new UploadedFile(file.FileName, file.Length, file.OpenReadStream());
    }

    // 沒有附件時表單綁定的結果是 null，當成空清單
    public static List<UploadedFile> ToUploadedFiles(this IEnumerable<IFormFile>? files)
    {
        return (files ?? []).Select(f => f.ToUploadedFile()).ToList();
    }
}
