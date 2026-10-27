namespace CaseTrack.Domain.Cases;

// 案件的附件（中繼資料）。檔案內容不在這裡：存在檔案系統，檔名就是 Id（不用使用者給的檔名）。
// 屬於 Case 聚合，只能透過 Case 的建構子或 SubmitSupplement 新增；送出後不能刪除或修改（陳情的紀錄）
public class Attachment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? SupplementRequestId { get; private set; } // null = 送件時附的；有值 = 哪一次補件附的
    public string FileName { get; private set; }            // 使用者給的原始檔名，只用來顯示與下載時的檔名
    public string ContentType { get; private set; }
    public long Size { get; private set; }                  // bytes；.NET 的檔案大小一律是 long（IFormFile.Length、Stream.Length）
    public DateTimeOffset UploadedAt { get; private set; }

    // 檢查規則的是 AttachmentRules（由 Case 呼叫），這裡只防程式錯誤
    internal Attachment(Guid? supplementRequestId,
        string fileName,
        string contentType,
        long size,
        DateTimeOffset uploadedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        SupplementRequestId = supplementRequestId;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        UploadedAt = uploadedAt;
    }
}
