using CaseTrack.Infrastructure.Storage;

namespace CaseTrack.IntegrationTests;

// 碰的是真的檔案系統，但不需要資料庫。每個測試用自己的暫存資料夾，結束時刪掉
public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CaseTrackTests", "storage", Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _storage = new LocalFileStorage(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ── 存檔與讀取 ───────────────────────────────────────

    // 內容比 FileStream 的緩衝區（80 KB）大，確保真的經過多次寫入，不是只留在緩衝區
    [Fact]
    public async Task SaveAsync_ThenOpenReadAsync_ReturnsSameBytes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var bytes = RandomBytes(200 * 1024);

        await _storage.SaveAsync(id, new MemoryStream(bytes), cancellationToken);

        Assert.Equal(bytes, await ReadAllAsync(id));
    }

    // 存放的檔名就是 Id，不加副檔名：伺服器和瀏覽器都無法從檔名判斷它是什麼
    [Fact]
    public async Task SaveAsync_StoresFileNamedByIdWithoutExtension()
    {
        var id = Guid.NewGuid();

        await _storage.SaveAsync(id, new MemoryStream([1, 2, 3]), TestContext.Current.CancellationToken);

        var file = Assert.Single(Directory.GetFiles(_root));
        Assert.Equal(id.ToString("N"), Path.GetFileName(file));
    }

    [Fact]
    public async Task OpenReadAsync_WhenNotExists_ReturnsNull()
    {
        Assert.Null(await _storage.OpenReadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    // FileShare.Read：很多人可以同時下載同一個附件
    [Fact]
    public async Task OpenReadAsync_TwiceAtTheSameTime_BothSucceed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await _storage.SaveAsync(id, new MemoryStream([1, 2, 3]), cancellationToken);

        await using var first = await _storage.OpenReadAsync(id, cancellationToken);
        await using var second = await _storage.OpenReadAsync(id, cancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    // ── 不能覆蓋 ─────────────────────────────────────────

    // 第二次失敗，而且第一次的檔案還在、內容沒變：失敗時只刪「這次建立的檔案」，不能刪到別人的
    [Fact]
    public async Task SaveAsync_SameIdTwice_ThrowsAndKeepsOriginalFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        byte[] original = [1, 2, 3];
        await _storage.SaveAsync(id, new MemoryStream(original), cancellationToken);

        await Assert.ThrowsAsync<IOException>(
            () => _storage.SaveAsync(id, new MemoryStream([9, 9, 9, 9]), cancellationToken));

        Assert.Equal(original, await ReadAllAsync(id));
    }

    // ── 失敗時不留下寫了一半的檔案 ───────────────────────

    // 來源讀到一半失敗（例如上傳的連線中斷）：原本的例外要丟出去，而且不留下檔案
    [Fact]
    public async Task SaveAsync_WhenSourceFailsMidway_RethrowsAndLeavesNoFile()
    {
        var id = Guid.NewGuid();
        var source = new FailingStream(bytesBeforeFailure: 200 * 1024);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _storage.SaveAsync(id, source, TestContext.Current.CancellationToken));

        Assert.Equal(FailingStream.Message, exception.Message);
        Assert.True(source.Position > 0, "測試要在已經寫入一部分之後才失敗");
        Assert.Empty(Directory.GetFiles(_root));
    }

    // 請求被取消（使用者關掉瀏覽器）時丟的是 OperationCanceledException，不是 IOException，一樣不能留下檔案
    [Fact]
    public async Task SaveAsync_WhenCanceled_LeavesNoFile()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _storage.SaveAsync(Guid.NewGuid(), new MemoryStream(RandomBytes(1024)), cts.Token));

        Assert.Empty(Directory.GetFiles(_root));
    }

    // ── 刪除 ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await _storage.SaveAsync(id, new MemoryStream([1, 2, 3]), cancellationToken);

        await _storage.DeleteAsync(id, cancellationToken);

        Assert.Null(await _storage.OpenReadAsync(id, cancellationToken));
    }

    // 補償時檔案可能根本沒存成功，刪除不存在的檔案不能變成另一個錯誤
    [Fact]
    public async Task DeleteAsync_WhenNotExists_DoesNotThrow()
    {
        await _storage.DeleteAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
    }

    // ── 建構子 ───────────────────────────────────────────

    // 相對路徑會相對於「目前的工作目錄」，隨啟動方式而不同（IIS 常是 System32）
    [Theory]
    [InlineData("App_Data/attachments")]
    [InlineData("attachments")]
    [InlineData("")]
    public void Constructor_WithRelativePath_Throws(string rootPath)
    {
        Assert.Throws<ArgumentException>(() => new LocalFileStorage(rootPath));
    }

    [Fact]
    public void Constructor_WhenDirectoryMissing_CreatesIt()
    {
        var nested = Path.Combine(_root, "a", "b");

        _ = new LocalFileStorage(nested);

        Assert.True(Directory.Exists(nested));
    }

    // ── Helpers ──────────────────────────────────────────

    private async Task<byte[]> ReadAllAsync(Guid id)
    {
        await using var stream = await _storage.OpenReadAsync(id, TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, TestContext.Current.CancellationToken);
        return buffer.ToArray();
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    // 讀到指定的位置之後丟例外，模擬上傳的連線中斷。
    // 不繼承 MemoryStream：MemoryStream 自己實作了 CopyToAsync，會直接複製整個內容，不經過 ReadAsync
    private sealed class FailingStream(int bytesBeforeFailure) : Stream
    {
        public const string Message = "連線中斷";
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_position >= bytesBeforeFailure)
            {
                throw new InvalidOperationException(Message);
            }
            var count = (int)Math.Min(buffer.Length, bytesBeforeFailure - _position);
            buffer[..count].Fill(0x42);
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer.AsSpan(offset, count)));

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
