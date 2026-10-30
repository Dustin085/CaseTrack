using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Cases;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Tests;

public class AttachmentUploaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Pdf = "%PDF-1.7 body"u8.ToArray();

    private readonly FakeFileStorage _storage = new();
    private readonly AttachmentUploader _uploader;

    public AttachmentUploaderTests()
    {
        _uploader = new AttachmentUploader(_storage);
    }

    // ── InspectAsync ─────────────────────────────────────

    // ContentType 用檔頭判斷出來的值；Size 是上傳時給的大小
    [Fact]
    public async Task Inspect_ValidFiles_ReturnsDetectedContentTypesInOrder()
    {
        var result = await _uploader.InspectAsync(
            [File("現場.jpg", Jpeg), File("地圖.PNG", Png), File("說明.pdf", Pdf)], Ct);

        Assert.Equal(["現場.jpg", "地圖.PNG", "說明.pdf"], result.Select(a => a.FileName));
        Assert.Equal(["image/jpeg", "image/png", "application/pdf"], result.Select(a => a.ContentType));
        Assert.Equal([Jpeg.Length, Png.Length, Pdf.Length], result.Select(a => a.Size));
    }

    [Fact]
    public async Task Inspect_NoFiles_ReturnsEmpty()
    {
        Assert.Empty(await _uploader.InspectAsync([], Ct));
    }

    // 舊版瀏覽器送出完整路徑：只留檔名，不保存使用者電腦的資料夾結構
    [Theory]
    [InlineData(@"C:\Users\王小明\Desktop\現場.jpg")]
    [InlineData("/home/user/現場.jpg")]
    public async Task Inspect_FileNameWithPath_KeepsOnlyFileName(string fileName)
    {
        var result = await _uploader.InspectAsync([File(fileName, Jpeg)], Ct);

        Assert.Equal("現場.jpg", Assert.Single(result).FileName);
    }

    // 改名成 .jpg 的執行檔：錯誤放在 Attachments 欄位，訊息說明是哪一個檔案
    [Fact]
    public async Task Inspect_UnknownContent_ThrowsValidationErrorNamingTheFile()
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => _uploader.InspectAsync([File("現場.jpg", Jpeg), File("photo.jpg", [0x4D, 0x5A, 0x90, 0x00])], Ct));

        Assert.Equal("Attachments", exception.Field);
        Assert.Contains("photo.jpg", exception.Message);
    }

    // 內容是 PNG，檔名是 .exe：下載後 Windows 會試著執行
    [Fact]
    public async Task Inspect_ExtensionDoesNotMatchContent_ThrowsValidationError()
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => _uploader.InspectAsync([File("photo.exe", Png)], Ct));

        Assert.Equal("Attachments", exception.Field);
        Assert.Contains("photo.exe", exception.Message);
    }

    // 超過數量上限：不用一個一個讀檔頭
    [Fact]
    public async Task Inspect_TooManyFiles_ThrowsValidationError()
    {
        var files = Enumerable.Range(1, AttachmentRules.MaxCountPerSubmission + 1)
            .Select(i => File($"{i}.jpg", Jpeg)).ToList();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() => _uploader.InspectAsync(files, Ct));
        Assert.Equal("Attachments", exception.Field);
    }

    // 大小由 Domain 的規則檢查：在取號之前就失敗
    [Fact]
    public async Task Inspect_TooLarge_ThrowsDomainValidationError()
    {
        await Assert.ThrowsAsync<InvalidAttachmentException>(
            () => _uploader.InspectAsync([new UploadedFile("大.jpg", AttachmentRules.MaxSize + 1, new MemoryStream(Jpeg))], Ct));
    }

    // 檢查完回到開頭：接著從頭存檔，少了檔頭的話每個附件都打不開
    [Fact]
    public async Task Inspect_LeavesStreamsAtStart()
    {
        var file = File("現場.jpg", Jpeg);

        await _uploader.InspectAsync([file], Ct);

        Assert.Equal(0, file.Content.Position);
    }

    // ── SaveThenCommitAsync：正常流程 ────────────────────

    // 每個檔案用對應的附件 Id 存檔，而且全部存完才 commit：commit 時檔案一定已經在了
    [Fact]
    public async Task SaveThenCommit_SavesEachFileUnderItsAttachmentIdBeforeCommit()
    {
        var (attachments, files) = Prepare(File("1.jpg", Jpeg), File("2.pdf", Pdf));
        IReadOnlyList<Guid>? savedWhenCommitted = null;

        await _uploader.SaveThenCommitAsync(attachments, files, _ =>
        {
            savedWhenCommitted = _storage.Saved.Keys.ToList();
            return Task.CompletedTask;
        }, Ct);

        Assert.Equal(attachments.Select(a => a.Id), savedWhenCommitted);
        Assert.Equal(Jpeg, _storage.Saved[attachments[0].Id]);
        Assert.Equal(Pdf, _storage.Saved[attachments[1].Id]);
        Assert.Empty(_storage.Deleted);
    }

    [Fact]
    public async Task SaveThenCommit_NoFiles_StillCommits()
    {
        var committed = false;

        await _uploader.SaveThenCommitAsync([], [], _ => { committed = true; return Task.CompletedTask; }, Ct);

        Assert.True(committed);
    }

    [Fact]
    public async Task SaveThenCommit_CountMismatch_ThrowsBeforeSaving()
    {
        var (attachments, _) = Prepare(File("1.jpg", Jpeg), File("2.jpg", Jpeg));

        await Assert.ThrowsAsync<ArgumentException>(
            () => _uploader.SaveThenCommitAsync(attachments, [File("1.jpg", Jpeg)], _ => Task.CompletedTask, Ct));
        Assert.Empty(_storage.Saved);
    }

    // ── SaveThenCommitAsync：失敗時的補償 ────────────────

    // 資料庫存檔失敗（例如補件時遇到 409）：刪掉全部檔案，原本的例外往外丟
    [Fact]
    public async Task SaveThenCommit_WhenCommitFails_DeletesAllSavedFilesAndRethrows()
    {
        var (attachments, files) = Prepare(File("1.jpg", Jpeg), File("2.png", Png));
        var original = new ConcurrencyConflictException("案件已被其他請求修改", new Exception());

        var thrown = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => _uploader.SaveThenCommitAsync(attachments, files, _ => throw original, Ct));

        Assert.Same(original, thrown);
        Assert.Equal(attachments.Select(a => a.Id).Order(), _storage.Deleted.Order());
    }

    // 存到第 2 個失敗：只刪第 1 個（第 3 個根本還沒存），而且不會 commit
    [Fact]
    public async Task SaveThenCommit_WhenSecondSaveFails_DeletesOnlyTheFirstAndDoesNotCommit()
    {
        var (attachments, files) = Prepare(File("1.jpg", Jpeg), File("2.jpg", Jpeg), File("3.jpg", Jpeg));
        _storage.FailOnSave = attachments[1].Id;
        var committed = false;

        await Assert.ThrowsAsync<IOException>(() => _uploader.SaveThenCommitAsync(
            attachments, files, _ => { committed = true; return Task.CompletedTask; }, Ct));

        Assert.False(committed);
        Assert.Equal([attachments[0].Id], _storage.Deleted);
    }

    // 請求被取消：原本的 token 已經取消，補償必須用另一個（CancellationToken.None），否則一個都刪不掉
    [Fact]
    public async Task SaveThenCommit_WhenCanceled_CompensatesWithoutTheCanceledToken()
    {
        var (attachments, files) = Prepare(File("1.jpg", Jpeg));
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _uploader.SaveThenCommitAsync(
            attachments, files, async token => { await cts.CancelAsync(); token.ThrowIfCancellationRequested(); }, cts.Token));

        Assert.Equal([attachments[0].Id], _storage.Deleted);
        Assert.All(_storage.DeleteTokens, token => Assert.False(token.IsCancellationRequested));
    }

    // 刪除也失敗：丟出的仍然是原本的例外（不能被刪除的錯誤蓋掉），其他檔案照樣刪
    [Fact]
    public async Task SaveThenCommit_WhenDeleteAlsoFails_RethrowsOriginalAndKeepsDeletingOthers()
    {
        var (attachments, files) = Prepare(File("1.jpg", Jpeg), File("2.jpg", Jpeg), File("3.jpg", Jpeg));
        _storage.FailOnDelete = attachments[0].Id;

        var thrown = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => _uploader.SaveThenCommitAsync(attachments, files, _ => throw new ConcurrencyConflictException("案件已被其他請求修改", new Exception()), Ct));

        Assert.NotNull(thrown);
        Assert.Equal([attachments[1].Id, attachments[2].Id], _storage.Deleted);
    }

    // ── Helpers ──────────────────────────────────────────

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UploadedFile File(string fileName, byte[] content) =>
        new(fileName, content.Length, new MemoryStream(content));

    // Attachment 的建構子是 internal（只能透過 Case 建立），所以用一個案件產生附件和它們的 Id
    private static (IReadOnlyList<Attachment> Attachments, IReadOnlyList<UploadedFile> Files) Prepare(
        params UploadedFile[] files)
    {
        var newAttachments = files
            .Select(f => new NewAttachment(f.FileName, "image/jpeg", f.Size))
            .ToList();
        var @case = new Case(CaseNumber.Create(new DateOnly(2026, 10, 27), 1), "路面坑洞", "中山路有坑洞",
            new Petitioner("王小明", "0912345678", null, null), "u-citizen-1", Now, newAttachments);
        return (@case.Attachments.ToList(), files);
    }

    // 記錄存了什麼、刪了什麼；可以指定某個 Id 存檔或刪除時失敗
    private sealed class FakeFileStorage : IFileStorage
    {
        public Dictionary<Guid, byte[]> Saved { get; } = new();
        public List<Guid> Deleted { get; } = new();
        public List<CancellationToken> DeleteTokens { get; } = new();
        public Guid? FailOnSave { get; set; }
        public Guid? FailOnDelete { get; set; }

        public async Task SaveAsync(Guid id, Stream content, CancellationToken cancellationToken)
        {
            if (id == FailOnSave)
            {
                throw new IOException("磁碟空間不足");
            }
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Saved.Add(id, buffer.ToArray());
        }

        public Task<Stream?> OpenReadAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(Saved.TryGetValue(id, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            DeleteTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (id == FailOnDelete)
            {
                throw new IOException("檔案被其他程式鎖住");
            }
            Deleted.Add(id);
            Saved.Remove(id);
            return Task.CompletedTask;
        }
    }
}
