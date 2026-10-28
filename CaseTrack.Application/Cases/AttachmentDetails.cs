using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

// 詳情裡的附件（不含檔案內容，下載另外呼叫 GetAttachmentHandler）。
// 承辦端和民眾端共用：附件本身沒有只給承辦人看的欄位
public sealed record AttachmentDetails(
    Guid Id,
    string FileName,
    string ContentType,
    long Size,
    DateTimeOffset UploadedAt)
{
    // supplementRequestId 是 null：送件時附的；有值：那一次補件附的。
    // 從資料庫讀出的附件沒有順序保證；同一次上傳的 UploadedAt 都一樣，再用檔名排序，每次的順序才會固定
    internal static IReadOnlyList<AttachmentDetails> ListFor(IEnumerable<Attachment> attachments, Guid? supplementRequestId) =>
        attachments
            .Where(a => a.SupplementRequestId == supplementRequestId)
            .OrderBy(a => a.UploadedAt)
            .ThenBy(a => a.FileName)
            .Select(a => new AttachmentDetails(a.Id, a.FileName, a.ContentType, a.Size, a.UploadedAt))
            .ToList();
}
