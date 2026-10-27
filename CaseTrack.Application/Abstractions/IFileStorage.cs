namespace CaseTrack.Application.Abstractions;

// 附件檔案內容的存放位置（資料庫只存中繼資料）。用 Guid 當鍵，不收路徑字串：呼叫端沒辦法指定任意路徑。
// 之後換成 NAS 或物件儲存，只要換掉實作，實作必須遵守下面每個方法的約定
public interface IFileStorage
{
    // 已經存在時丟例外（不會覆蓋）；失敗時不留下寫了一半的檔案
    Task SaveAsync(Guid id, Stream content, CancellationToken cancellationToken);

    // 不存在 → null。回傳的 Stream 由呼叫端負責 Dispose
    Task<Stream?> OpenReadAsync(Guid id, CancellationToken cancellationToken);

    // 補償用（檔案存好了但資料庫儲存失敗）；不存在不算錯誤
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
