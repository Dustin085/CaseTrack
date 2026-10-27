namespace CaseTrack.Application.Cases;

// 用檔案開頭的幾個 bytes（檔頭，magic number）判斷實際的類型。
// 副檔名和瀏覽器送來的 Content-Type 都是使用者自己說的（把 virus.exe 改名成 photo.jpg 就騙過去了），不能相信
public static class FileSignatures
{
    // 檔頭最長的是 PNG 的 8 bytes
    private const int MaxSignatureLength = 8;

    private static readonly Signature[] Signatures =
    [
        new("image/jpeg", [0xFF, 0xD8, 0xFF], [".jpg", ".jpeg"]),
        new("image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], [".png"]),
        new("application/pdf", "%PDF-"u8.ToArray(), [".pdf"]),
    ];

    // 認得出來的類型。必須和 Domain 的 AttachmentRules.AllowedContentTypes 一致（有測試檢查）：
    // 那邊允許了這裡卻認不出來的類型，就永遠上傳不了，而且不會有任何錯誤說明原因
    public static IReadOnlyList<string> KnownContentTypes { get; } = Signatures.Select(s => s.ContentType).ToArray();

    // 認不出來 → null。讀完會回到開頭（Position = 0）：呼叫端接著要從頭存檔，少了開頭的 bytes 檔案就壞了
    public static async Task<string?> DetectContentTypeAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            // 沒辦法回到開頭的話，讀過的 8 bytes 就存不回去
            throw new ArgumentException("需要可以回到開頭的 Stream", nameof(stream));
        }

        stream.Position = 0;
        var buffer = new byte[MaxSignatureLength];
        // ReadAsync 不保證一次讀滿，用 ReadAtLeastAsync；檔案比 8 bytes 短時讀到多少算多少
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        stream.Position = 0;

        // 只比對真正讀到的部分：檔案太短時 buffer 後面是 0，不是檔案的內容
        var header = buffer.AsSpan(0, read);
        foreach (var signature in Signatures)
        {
            if (header.StartsWith(signature.Bytes))
            {
                return signature.ContentType;
            }
        }
        return null;
    }

    // 副檔名是否符合實際的類型（不分大小寫）。內容是 PNG、檔名卻是 photo.exe 也要拒絕：
    // 下載時檔名照原樣給使用者，Windows 看到 .exe 會試著執行它
    public static bool ExtensionMatches(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName);
        return Signatures.Any(s => s.ContentType == contentType
            && s.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    // private：record 只保證屬性不能換成別的陣列，陣列的內容還是改得動，不能讓外面碰到
    private sealed record Signature(string ContentType, byte[] Bytes, string[] Extensions);
}
