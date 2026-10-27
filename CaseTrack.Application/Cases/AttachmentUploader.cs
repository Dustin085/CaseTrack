using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

// 送件與補件共用的附件流程：檢查 → 存檔 → 存資料庫 → 失敗時刪掉剛存的檔案。
// 檔案系統和資料庫沒辦法放在同一個交易裡，所以安排順序：先存檔、再存資料庫。
// 這樣失敗時最多留下沒人引用的檔案（無害），不會出現「資料庫有紀錄、檔案卻不存在」（下載會壞掉）
public sealed class AttachmentUploader
{
    private const string Field = "Attachments";

    private readonly IFileStorage _fileStorage;

    public AttachmentUploader(IFileStorage fileStorage)
    {
        _fileStorage = fileStorage;
    }

    // 用檔頭決定實際的類型，轉成 Domain 的 NewAttachment；瀏覽器送來的 Content-Type 完全不使用。
    // 不合法 → RequestValidationException（400，錯誤顯示在附件欄位），訊息包含是哪一個檔案
    public async Task<IReadOnlyList<NewAttachment>> InspectAsync(
        IReadOnlyList<UploadedFile> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        // 數量先檢查：超過上限就不用一個一個讀檔頭
        if (files.Count > AttachmentRules.MaxCountPerSubmission)
        {
            throw new RequestValidationException(Field, $"附件最多 {AttachmentRules.MaxCountPerSubmission} 個");
        }

        var result = new List<NewAttachment>(files.Count);
        foreach (var file in files)
        {
            // 只取檔名：舊版瀏覽器會送完整路徑（C:\Users\王小明\Desktop\現場.jpg），會洩漏使用者電腦的資料夾結構。
            // 先把 \ 換成 /：Linux 上的 Path.GetFileName 只認 /，伺服器換成 Linux 時 Windows 的路徑會整串留下來
            var fileName = Path.GetFileName(file.FileName.Replace('\\', '/'));

            var contentType = await FileSignatures.DetectContentTypeAsync(file.Content, cancellationToken)
                ?? throw new RequestValidationException(Field, $"不支援的檔案類型：{fileName}（只接受 JPG、PNG、PDF）");
            if (!FileSignatures.ExtensionMatches(fileName, contentType))
            {
                throw new RequestValidationException(Field, $"副檔名和檔案內容不符：{fileName}");
            }

            result.Add(new NewAttachment(fileName, contentType, file.Size));
        }

        // Domain 之後還會再檢查一次；在這裡先檢查，不合法就不會浪費一個案件編號（同 Petitioner 先建立的理由）
        AttachmentRules.Validate(result);
        return result;
    }

    // attachments 是 Domain 建立的附件（Id 就是檔名），順序和 files 相同。
    // commit 由呼叫端傳入（_unitOfWork.SaveChangesAsync）：這裡只需要知道「存檔之後要做一件可能失敗的事」
    public async Task SaveThenCommitAsync(
        IReadOnlyList<Attachment> attachments,
        IReadOnlyList<UploadedFile> files,
        Func<CancellationToken, Task> commit,
        CancellationToken cancellationToken)
    {
        if (attachments.Count != files.Count)
        {
            throw new ArgumentException("附件和檔案的數量不一致", nameof(files));
        }

        var saved = new List<Guid>(attachments.Count);
        try
        {
            foreach (var (attachment, file) in attachments.Zip(files))
            {
                await _fileStorage.SaveAsync(attachment.Id, file.Content, cancellationToken);
                saved.Add(attachment.Id);
            }
            await commit(cancellationToken);
        }
        catch
        {
            await DeleteQuietlyAsync(saved);
            throw;
        }
    }

    // 補償：盡力刪除，不讓刪除的錯誤蓋掉原本的例外。
    // 用 CancellationToken.None：請求被取消時原本的 token 已經取消了，拿它去刪會馬上失敗，一個都刪不掉。
    // 刪不掉（或程式在這之前就當掉）會留下孤兒檔案，之後由定期對帳清理
    private async Task DeleteQuietlyAsync(IEnumerable<Guid> ids)
    {
        foreach (var id in ids)
        {
            try
            {
                await _fileStorage.DeleteAsync(id, CancellationToken.None);
            }
            catch (Exception)
            {
                // 每個檔案各自處理：一個刪不掉，其他的還是要刪
            }
        }
    }
}
