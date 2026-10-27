using System.Collections.Frozen;

namespace CaseTrack.Domain.Cases;

// 附件的業務規則：允許的類型、大小、數量。
// 檔頭檢查（內容是不是真的是這個類型）要讀檔案內容，在 Application 做；這裡只檢查中繼資料
public static class AttachmentRules
{
    public const long MaxSize = 10 * 1024 * 1024;       // 單檔 10 MB
    public const int MaxCountPerSubmission = 5;          // 每次送件或補件最多 5 個
    public const int MaxFileNameLength = 255;            // 和大多數檔案系統的上限一致

    // 不收 Office 檔（可能帶巨集、驗證也比較複雜）；FrozenSet 的理由同 Roles：全域共用，必須真的不能改
    public static readonly FrozenSet<string> AllowedContentTypes =
        new[] { "image/jpeg", "image/png", "application/pdf" }.ToFrozenSet();

    public static void Validate(IReadOnlyList<NewAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        if (attachments.Count > MaxCountPerSubmission)
        {
            throw new InvalidAttachmentException($"附件最多 {MaxCountPerSubmission} 個");
        }

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.FileName))
            {
                throw new InvalidAttachmentException("附件的檔名不能是空的");
            }
            if (attachment.FileName.Length > MaxFileNameLength)
            {
                throw new InvalidAttachmentException($"附件的檔名不能超過 {MaxFileNameLength} 個字");
            }
            if (!AllowedContentTypes.Contains(attachment.ContentType))
            {
                throw new InvalidAttachmentException($"不支援的檔案類型：{attachment.FileName}（只接受 JPG、PNG、PDF）");
            }
            // 0 byte 的檔案通常是選錯檔案或上傳中斷，沒有保存的意義
            if (attachment.Size <= 0)
            {
                throw new InvalidAttachmentException($"附件是空的：{attachment.FileName}");
            }
            if (attachment.Size > MaxSize)
            {
                throw new InvalidAttachmentException($"附件不能超過 {MaxSize / 1024 / 1024} MB：{attachment.FileName}");
            }
        }
    }
}
