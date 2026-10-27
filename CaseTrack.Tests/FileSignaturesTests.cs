using CaseTrack.Application.Cases;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Tests;

public class FileSignaturesTests
{
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PdfHeader = "%PDF-1.7"u8.ToArray();

    // ── 認得出來的類型 ───────────────────────────────────

    public static TheoryData<byte[], string> KnownFiles => new()
    {
        { WithBody(JpegHeader), "image/jpeg" },
        { WithBody(PngHeader), "image/png" },
        { WithBody(PdfHeader), "application/pdf" },
    };

    [Theory]
    [MemberData(nameof(KnownFiles))]
    public async Task Detect_KnownHeader_ReturnsContentType(byte[] content, string expected)
    {
        Assert.Equal(expected, await DetectAsync(content));
    }

    // 剛好只有檔頭那麼長（JPG 的 3 bytes）也認得出來：讀到多少算多少，不要求一定要讀滿 8 bytes
    [Fact]
    public async Task Detect_FileExactlyAsLongAsJpegHeader_ReturnsJpeg()
    {
        Assert.Equal("image/jpeg", await DetectAsync([0xFF, 0xD8, 0xFF]));
    }

    // ReadAsync 不保證一次讀滿：Stream 一次只給 1 個 byte，還是要讀到完整的檔頭
    [Fact]
    public async Task Detect_StreamReturningOneBytePerRead_StillReadsWholeHeader()
    {
        await using var stream = new OneBytePerReadStream(WithBody(PngHeader));

        Assert.Equal("image/png", await FileSignatures.DetectContentTypeAsync(stream, TestContext.Current.CancellationToken));
    }

    // ── 認不出來的內容 ───────────────────────────────────

    public static TheoryData<string, byte[]> UnknownFiles => new()
    {
        { "Windows 執行檔改名成 .jpg", WithBody("MZ"u8.ToArray()) },
        { "偽裝成圖片的網頁", "<html><script>alert(1)</script></html>"u8.ToArray() },
        { "ZIP（.docx、.xlsx 都是 ZIP）", WithBody([0x50, 0x4B, 0x03, 0x04]) },
        { "GIF（沒有開放的類型）", WithBody("GIF89a"u8.ToArray()) },
        { "SVG（文字檔，可以內嵌 script）", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray() },
        { "PNG 檔頭最後一個 byte 不對", WithBody([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x00]) },
        { "只有 JPG 檔頭的前 2 bytes", [0xFF, 0xD8] },
        { "沒有 '-' 的 %PDF", WithBody("%PDF"u8.ToArray()) },
        { "空檔案", [] },
    };

    [Theory]
    [MemberData(nameof(UnknownFiles))]
    public async Task Detect_UnknownContent_ReturnsNull(string description, byte[] content)
    {
        _ = description; // 只是讓測試結果看得懂是哪一種
        Assert.Null(await DetectAsync(content));
    }

    // 檔頭一定要在最前面：前面多了任何東西就不算（不在檔案中間搜尋）
    [Fact]
    public async Task Detect_HeaderNotAtStart_ReturnsNull()
    {
        Assert.Null(await DetectAsync([0x00, .. JpegHeader]));
    }

    // ── 讀完回到開頭 ─────────────────────────────────────

    // 接著要從頭存檔：沒有回到開頭的話，存下來的檔案少了檔頭，每個附件都打不開
    [Fact]
    public async Task Detect_LeavesPositionAtStart()
    {
        await using var stream = new MemoryStream(WithBody(PngHeader));

        await FileSignatures.DetectContentTypeAsync(stream, TestContext.Current.CancellationToken);

        Assert.Equal(0, stream.Position);
    }

    // 呼叫前已經被讀過一部分：一樣從頭判斷
    [Fact]
    public async Task Detect_WhenPositionNotAtStart_ReadsFromStart()
    {
        await using var stream = new MemoryStream(WithBody(JpegHeader));
        stream.Position = 5;

        Assert.Equal("image/jpeg", await FileSignatures.DetectContentTypeAsync(stream, TestContext.Current.CancellationToken));
        Assert.Equal(0, stream.Position);
    }

    // 回不到開頭的 Stream 不能用：讀過的檔頭存不回去，與其存出壞掉的檔案，不如馬上失敗
    [Fact]
    public async Task Detect_NonSeekableStream_Throws()
    {
        await using var stream = new NonSeekableStream(WithBody(JpegHeader));

        await Assert.ThrowsAsync<ArgumentException>(
            () => FileSignatures.DetectContentTypeAsync(stream, TestContext.Current.CancellationToken));
    }

    // ── 副檔名 ───────────────────────────────────────────

    [Theory]
    [InlineData("現場.jpg", "image/jpeg")]
    [InlineData("現場.jpeg", "image/jpeg")]
    [InlineData("PHOTO.JPG", "image/jpeg")]
    [InlineData("map.Png", "image/png")]
    [InlineData("說明.pdf", "application/pdf")]
    [InlineData("report.v2.pdf", "application/pdf")]
    public void ExtensionMatches_MatchingExtension_ReturnsTrue(string fileName, string contentType)
    {
        Assert.True(FileSignatures.ExtensionMatches(fileName, contentType));
    }

    [Theory]
    [InlineData("photo.exe", "image/png")]      // 內容是 PNG，檔名是 .exe：下載後 Windows 會試著執行
    [InlineData("photo.jpg.exe", "image/jpeg")] // 只看最後一個副檔名
    [InlineData("photo.png", "image/jpeg")]     // 都是圖片也不行：副檔名要和實際的類型一致
    [InlineData("photo", "image/jpeg")]         // 沒有副檔名
    [InlineData("photo.", "image/jpeg")]
    [InlineData("photo.gif", "image/gif")]      // 不認得的類型
    public void ExtensionMatches_MismatchedExtension_ReturnsFalse(string fileName, string contentType)
    {
        Assert.False(FileSignatures.ExtensionMatches(fileName, contentType));
    }

    // ── 與 Domain 的規則一致 ─────────────────────────────

    // 允許的類型寫在兩個地方（Domain 的規則、這裡的檔頭對照表）：
    // Domain 允許了但這裡認不出來 → 永遠上傳不了；這裡認得但 Domain 不允許 → 對照表裡有用不到的資料
    [Fact]
    public void KnownContentTypes_MatchAttachmentRules()
    {
        Assert.Equal(
            AttachmentRules.AllowedContentTypes.Order(),
            FileSignatures.KnownContentTypes.Order());
    }

    // ── Helpers ──────────────────────────────────────────

    private static async Task<string?> DetectAsync(byte[] content)
    {
        await using var stream = new MemoryStream(content);
        return await FileSignatures.DetectContentTypeAsync(stream, TestContext.Current.CancellationToken);
    }

    // 檔頭後面接一段內容，像一個真的檔案
    private static byte[] WithBody(byte[] header) => [.. header, .. new byte[64]];

    // 一次最多只給 1 個 byte（網路傳輸時很常見：資料分批到達）
    private sealed class OneBytePerReadStream(byte[] content) : MemoryStream(content)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(1, buffer.Length)]);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }

    private sealed class NonSeekableStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }
}
