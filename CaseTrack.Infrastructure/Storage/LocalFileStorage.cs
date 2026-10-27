using CaseTrack.Application.Abstractions;

namespace CaseTrack.Infrastructure.Storage;

// 把附件存在本機（或掛載的網路磁碟）的資料夾。檔名就是附件的 Id（不加副檔名、不用使用者給的檔名），
// 呼叫端只能傳 Guid，沒辦法組出 "../../web.config" 這種路徑
public class LocalFileStorage : IFileStorage
{
    private const int BufferSize = 81920; // 和 CopyToAsync 的預設值相同，且小於大型物件堆積（LOH）的 85,000 bytes

    private readonly string _rootPath;

    public LocalFileStorage(string rootPath)
    {
        // 相對路徑會相對於「目前的工作目錄」，隨啟動方式而不同；轉成絕對路徑是呼叫端（Program.cs）的責任
        if (!Path.IsPathFullyQualified(rootPath))
        {
            throw new ArgumentException($"附件的存放位置必須是絕對路徑：{rootPath}", nameof(rootPath));
        }
        _rootPath = rootPath;
        Directory.CreateDirectory(_rootPath); // 已經存在時不會出錯
    }

    public async Task SaveAsync(Guid id, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathOf(id);
        var created = false;

        try
        {
            // CreateNew：已經存在就失敗，檢查和建立由作業系統一次完成，絕對不會覆蓋
            await using (var file = new FileStream(path,
                FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferSize, useAsync: true))
            {
                created = true;
                await content.CopyToAsync(file, cancellationToken);
            } // 這裡會把緩衝區剩下的資料寫進硬碟並關閉檔案；寫入失敗一樣會進到下面的 catch
        }
        // 只有「這次建立了檔案」才刪：檔案原本就存在（CreateNew 失敗）時，那是別人的檔案，不能刪
        catch when (created)
        {
            // 刪除失敗只會留下一個沒人引用的檔案；不能讓它蓋掉原本的例外
            try { File.Delete(path); } catch (IOException) { }
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = PathOf(id);

        // 直接開啟、接住「不存在」，不先 File.Exists：檢查和開啟之間檔案可能剛好被刪掉
        try
        {
            Stream file = new FileStream(path,
                FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, useAsync: true);
            return Task.FromResult<Stream?>(file);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        File.Delete(PathOf(id)); // 不存在時不會丟例外
        return Task.CompletedTask;
    }

    private string PathOf(Guid id) => Path.Combine(_rootPath, id.ToString("N"));
}
