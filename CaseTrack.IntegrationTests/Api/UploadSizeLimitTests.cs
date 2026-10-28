using System.Net;
using CaseTrack.Domain.Cases;
using System.Text.Json;
using CaseTrack.Api.Common;
using static CaseTrack.IntegrationTests.Api.ApiTestHelpers;

namespace CaseTrack.IntegrationTests.Api;

// 跑在真的 Kestrel 上：一般的 API 測試用的 TestServer 完全不套用 [RequestSizeLimit]，
// 超過上限的請求會整個收下來，最後變成 Domain 的「單檔超過 10 MB」（400），測不到 413
public sealed class KestrelApiFactory : CaseTrackApiFactory
{
    public KestrelApiFactory()
    {
        UseKestrel(0); // 0：由作業系統挑一個沒用到的 port
    }
}

// 整個請求超過上限：一邊收一邊數，超過就中斷，回 413（不會收完再交給 Domain 檢查）。
// 錯誤放在 Attachments 欄位（和其他附件錯誤同一個地方），不是模型繫結預設的空字串欄位
[Collection(nameof(DatabaseCollection))]
public class UploadSizeLimitTests : IClassFixture<KestrelApiFactory>
{
    private readonly KestrelApiFactory _factory;
    private readonly HttpClient _citizenClient;

    public UploadSizeLimitTests(KestrelApiFactory factory)
    {
        _factory = factory;
        _factory.StartServer();
        _citizenClient = factory.CreateCitizenClient();
        // Kestrel 回 413 之後就關閉連線；沒有這個的話 HttpClient 還在上傳剩下的 body，寫入失敗，讀不到回應。
        // 100-continue：先只送標頭，等伺服器同意才送 body。Content-Length 已經超過上限時，Kestrel 直接回 413
        _citizenClient.DefaultRequestHeaders.ExpectContinue = true;
    }

    [Fact]
    public async Task SubmitCase_RequestOverBodyLimit_ReturnsPayloadTooLargeOnAttachments()
    {
        var response = await _citizenClient.PostCaseAsync(CaseForm(files: [OverLimitFile()]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var message = Assert.Single(problem.RootElement.GetProperty("errors").GetProperty("Attachments").EnumerateArray());
        Assert.StartsWith("附件總大小超過上限", message.GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _), "錯誤回應要帶 traceId，才能從民眾回報追到 log");
    }

    // 補件不用真的在等補件：大小在模型繫結之前就檢查，案件存不存在都還沒查
    [Fact]
    public async Task SubmitSupplement_RequestOverBodyLimit_ReturnsPayloadTooLarge()
    {
        var response = await _citizenClient.PostSupplementAsync(Guid.NewGuid(), SupplementForm("補件", OverLimitFile()));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // 正面對照：剛好在上限內（10 MB 的單檔），Kestrel 不會擋，請求會到達 Domain
    [Fact]
    public async Task SubmitCase_MaxSizeFile_IsNotBlockedByBodyLimit()
    {
        var file = new byte[AttachmentRules.MaxSize];
        JpegBytes.CopyTo(file, 0);

        var response = await _citizenClient.PostCaseAsync(CaseForm(files: [new TestFile("剛好.jpg", file)]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static TestFile OverLimitFile()
    {
        var huge = new byte[UploadLimits.MaxRequestBodySize + 1];
        JpegBytes.CopyTo(huge, 0);
        return new TestFile("超大.jpg", huge);
    }
}
