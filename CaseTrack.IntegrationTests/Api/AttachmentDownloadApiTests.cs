using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Cases;
using CaseTrack.Infrastructure.Persistence.Configurations;
using static CaseTrack.IntegrationTests.Api.ApiTestHelpers;

namespace CaseTrack.IntegrationTests.Api;

// 詳情裡的附件清單與下載：誰能下載（資料範圍、角色）、附件一定要屬於網址上的案件（IDOR）、回應的標頭
[Collection(nameof(DatabaseCollection))]
public class AttachmentDownloadApiTests : IClassFixture<CaseTrackApiFactory>
{
    private readonly CaseTrackApiFactory _factory;
    private readonly HttpClient _citizenClient;
    private readonly HttpClient _dispatcherClient;
    private readonly HttpClient _officerClient;

    public AttachmentDownloadApiTests(CaseTrackApiFactory factory)
    {
        _factory = factory;
        _citizenClient = factory.CreateCitizenClient();
        _dispatcherClient = factory.CreateDispatcherClient();
        _officerClient = factory.CreateOfficerClient();
    }

    // ── 詳情的附件清單 ───────────────────────────────────

    // 送件附的放在案件上，補件附的放在那一次補件裡；同一次上傳的依檔名排序。
    // 用 4 個檔案、不照檔名的順序送出：資料庫沒有 ORDER BY 時的順序不固定，只有 2 個的話一半的機會剛好排對，拿掉排序也測不出來
    [Fact]
    public async Task GetMyCase_ListsSubmissionAndSupplementAttachmentsSeparately()
    {
        var caseId = await SubmitCaseAsync(
            new TestFile("c-遠景.jpg", JpegBytes),
            new TestFile("a-位置.jpg", JpegBytes),
            new TestFile("d-近照.png", PngBytes),
            new TestFile("b-現場.png", PngBytes));
        await RequestSupplementAsync(caseId);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _citizenClient.PostSupplementAsync(caseId, SupplementForm("補上報告", new TestFile("報告.pdf", PdfBytes)))).StatusCode);

        using var details = await GetJsonAsync(_citizenClient, $"api/my/cases/{caseId}");

        AssertAttachmentNames(details.RootElement, "a-位置.jpg", "b-現場.png", "c-遠景.jpg", "d-近照.png");
        var supplement = Assert.Single(details.RootElement.GetProperty("supplementRequests").EnumerateArray());
        AssertAttachmentNames(supplement, "報告.pdf");
        var report = supplement.GetProperty("attachments")[0];
        Assert.Equal("application/pdf", report.GetProperty("contentType").GetString());
        Assert.Equal(PdfBytes.Length, report.GetProperty("size").GetInt64());
    }

    // 承辦端的詳情（不同的 DTO、不同的 Handler）也一樣
    [Fact]
    public async Task GetCase_ListsSubmissionAndSupplementAttachmentsSeparately()
    {
        var caseId = await SubmitCaseAsync(new TestFile("現場.jpg", JpegBytes));
        await RequestSupplementAsync(caseId);
        await _citizenClient.PostSupplementAsync(caseId, SupplementForm("補上地圖", new TestFile("地圖.png", PngBytes)));

        using var details = await GetJsonAsync(_officerClient, $"api/cases/{caseId}");

        AssertAttachmentNames(details.RootElement, "現場.jpg");
        AssertAttachmentNames(details.RootElement.GetProperty("supplementRequests")[0], "地圖.png");
    }

    // ── 下載 ─────────────────────────────────────────────

    // 內容原封不動；類型是檔頭判斷出來的；一律下載（attachment），中文檔名用 filename*；不快取、不猜類型
    [Fact]
    public async Task Citizen_DownloadsOwnAttachment_WithSafeHeaders()
    {
        var caseId = await SubmitCaseAsync(new TestFile("現場照片.jpg", JpegBytes));
        var attachmentId = await GetFirstAttachmentIdAsync(caseId);

        var response = await _citizenClient.GetAsync($"api/my/cases/{caseId}/attachments/{attachmentId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JpegBytes, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("現場照片.jpg", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(response.Headers.CacheControl?.NoStore, "附件可能含有個資，不能被快取");
    }

    // 補件附的也能下載
    [Fact]
    public async Task Citizen_DownloadsSupplementAttachment()
    {
        var caseId = await SubmitCaseAsync();
        await RequestSupplementAsync(caseId);
        await _citizenClient.PostSupplementAsync(caseId, SupplementForm("補上報告", new TestFile("報告.pdf", PdfBytes)));
        using var details = await GetJsonAsync(_citizenClient, $"api/my/cases/{caseId}");
        var attachmentId = details.RootElement.GetProperty("supplementRequests")[0].GetProperty("attachments")[0]
            .GetProperty("id").GetGuid();

        var response = await _citizenClient.GetAsync($"api/my/cases/{caseId}/attachments/{attachmentId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PdfBytes, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    // 別人的案件：和不存在一樣
    [Fact]
    public async Task OtherCitizen_CannotDownload_ReturnsNotFound()
    {
        var caseId = await SubmitCaseAsync(new TestFile("現場.jpg", JpegBytes));
        var attachmentId = await GetFirstAttachmentIdAsync(caseId);
        var otherCitizen = _factory.CreateCitizenClient("u-citizen-other");

        var response = await otherCitizen.GetAsync($"api/my/cases/{caseId}/attachments/{attachmentId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // IDOR：自己的案件 Id 配上別人案件的附件 Id。案件的資料範圍檢查會通過，附件必須從這個案件裡找才擋得下來
    [Fact]
    public async Task OwnCaseWithOtherCasesAttachmentId_ReturnsNotFound()
    {
        var victimClient = _factory.CreateCitizenClient("u-citizen-victim");
        var victimCaseId = await SubmitCaseAsync(victimClient, new TestFile("身分證.jpg", JpegBytes));
        var victimAttachmentId = await GetFirstAttachmentIdAsync(victimClient, victimCaseId);
        var ownCaseId = await SubmitCaseAsync();

        var response = await _citizenClient.GetAsync($"api/my/cases/{ownCaseId}/attachments/{victimAttachmentId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // 承辦端：本單位的承辦人可以；分派人員可以（還沒分派也要看得到照片，才知道該分給誰）
    [Fact]
    public async Task OfficerOfAssignedDepartmentAndDispatcher_CanDownload()
    {
        var caseId = await SubmitCaseAsync(new TestFile("現場.jpg", JpegBytes));
        var attachmentId = await GetFirstAttachmentIdAsync(caseId);
        var url = $"api/cases/{caseId}/attachments/{attachmentId}";

        var beforeAssignment = await _dispatcherClient.GetAsync(url, TestContext.Current.CancellationToken);
        await _dispatcherClient.AssignAsync(caseId, TestTokens.OfficerDepartmentId);
        var officerResponse = await _officerClient.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, beforeAssignment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, officerResponse.StatusCode);
        Assert.Equal(JpegBytes, await officerResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("nosniff", Assert.Single(officerResponse.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(officerResponse.Headers.CacheControl?.NoStore);
    }

    // 別的單位的承辦人：資料範圍之外，404（不透露案件存在）
    [Fact]
    public async Task OfficerOfOtherDepartment_ReturnsNotFound()
    {
        var caseId = await SubmitCaseAsync(new TestFile("現場.jpg", JpegBytes));
        var attachmentId = await GetFirstAttachmentIdAsync(caseId);
        await _dispatcherClient.AssignAsync(caseId, DepartmentSeed.PublicWorks);
        var otherOfficer = _factory.CreateClientFor("u-officer-tra", [Roles.CaseOfficer], DepartmentSeed.Transportation);

        var response = await otherOfficer.GetAsync($"api/cases/{caseId}/attachments/{attachmentId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // 管理者不能看案件內容：角色就擋下（403），不會走到資料範圍
    [Fact]
    public async Task Administrator_ReturnsForbidden()
    {
        var caseId = await SubmitCaseAsync(new TestFile("現場.jpg", JpegBytes));
        var attachmentId = await GetFirstAttachmentIdAsync(caseId);
        var administrator = _factory.CreateClientFor("u-admin-1", [Roles.Administrator], DepartmentSeed.PublicWorks);

        var response = await administrator.GetAsync($"api/cases/{caseId}/attachments/{attachmentId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // 民眾不能用承辦端的網址（即使是自己的案件）
    [Fact]
    public async Task Citizen_UsingStaffEndpoint_ReturnsForbidden()
    {
        var caseId = await SubmitCaseAsync(new TestFile("現場.jpg", JpegBytes));
        var attachmentId = await GetFirstAttachmentIdAsync(caseId);

        var response = await _citizenClient.GetAsync($"api/cases/{caseId}/attachments/{attachmentId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // 資料庫有紀錄、檔案卻不見了：是系統的問題，回 500，不能回 404 讓民眾以為附件不存在
    [Fact]
    public async Task StoredFileMissing_ReturnsServerError()
    {
        var caseId = await SubmitCaseAsync(new TestFile("現場.jpg", JpegBytes));
        var attachmentId = await GetFirstAttachmentIdAsync(caseId);
        File.Delete(Path.Combine(TestData.FileStorageRoot, attachmentId.ToString("N")));

        var response = await _citizenClient.GetAsync($"api/my/cases/{caseId}/attachments/{attachmentId}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────

    private Task<Guid> SubmitCaseAsync(params TestFile[] files) => SubmitCaseAsync(_citizenClient, files);

    private static async Task<Guid> SubmitCaseAsync(HttpClient citizenClient, params TestFile[] files)
    {
        var response = await citizenClient.PostCaseAsync(CaseForm(files: files));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var submitted = await response.Content.ReadFromJsonAsync<SubmitCaseResult>(TestContext.Current.CancellationToken);
        Assert.NotNull(submitted);
        return submitted.Id;
    }

    // 附件 Id 從民眾的詳情取得：和前端拿到 Id 的方式一樣
    private Task<Guid> GetFirstAttachmentIdAsync(Guid caseId) => GetFirstAttachmentIdAsync(_citizenClient, caseId);

    private static async Task<Guid> GetFirstAttachmentIdAsync(HttpClient citizenClient, Guid caseId)
    {
        using var details = await GetJsonAsync(citizenClient, $"api/my/cases/{caseId}");
        return details.RootElement.GetProperty("attachments")[0].GetProperty("id").GetGuid();
    }

    private async Task RequestSupplementAsync(Guid caseId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _dispatcherClient.AssignAsync(caseId, TestTokens.OfficerDepartmentId);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _officerClient.PostAsync($"api/cases/{caseId}/review", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _officerClient.PostAsJsonAsync($"api/cases/{caseId}/supplement-requests",
                new { reason = "請補資料" }, cancellationToken)).StatusCode);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static void AssertAttachmentNames(JsonElement owner, params string[] expected) =>
        Assert.Equal(expected,
            owner.GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("fileName").GetString()));
}
