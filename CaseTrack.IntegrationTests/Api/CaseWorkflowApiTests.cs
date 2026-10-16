using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CaseTrack.Application.Cases;
using static CaseTrack.IntegrationTests.Api.ApiTestHelpers;

namespace CaseTrack.IntegrationTests.Api;

// 透過 HTTP 測整個 API：路由、模型繫結、驗證、例外處理、JSON 序列化都會經過。
// 站在外部使用者的角度：請求用匿名型別，狀態比對字串（API 的合約），不依賴 Domain 的型別。
// 用三個 client 代表三種實際的使用者：沒有登入的民眾（送件、補件）、分派人員（分派）、工務局的承辦人（處理案件）。
[Collection(nameof(DatabaseCollection))]
public class CaseWorkflowApiTests : IClassFixture<CaseTrackApiFactory>
{
    private readonly HttpClient _citizenClient;
    private readonly HttpClient _dispatcherClient;
    private readonly HttpClient _officerClient;

    public CaseWorkflowApiTests(CaseTrackApiFactory factory)
    {
        _citizenClient = factory.CreateCitizenClient();
        _dispatcherClient = factory.CreateDispatcherClient();
        _officerClient = factory.CreateOfficerClient();
    }

    [Fact]
    public async Task FullWorkflow_FromSubmitToClose_EndsClosed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // 提交：201 + Location
        var submitResponse = await _citizenClient.PostAsJsonAsync("api/cases",
            new { subject = "路燈不亮", content = "中山路路燈故障", petitioner = ValidPetitioner },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<SubmitCaseResult>(cancellationToken);
        Assert.NotNull(submitted);
        Assert.Matches(@"^\d{8}-\d{4}$", submitted.CaseNumber);
        var location = submitResponse.Headers.Location;
        Assert.NotNull(location);

        // 處理流程：每一步都依賴前一步真的生效，任何一步沒生效，後面就會 409
        await _dispatcherClient.AssignAsync(submitted.Id, TestTokens.OfficerDepartmentId);
        await AssertNoContentAsync(PostActionAsync(_officerClient, submitted.Id, "review"));
        await AssertNoContentAsync(_officerClient.PostAsJsonAsync(
            $"api/cases/{submitted.Id}/supplement-requests", new { reason = "缺照片" }, cancellationToken));
        await AssertNoContentAsync(PostActionAsync(_citizenClient, submitted.Id, "supplement-submission"));
        await AssertNoContentAsync(PostActionAsync(_officerClient, submitted.Id, "close"));

        // 用 Location 取回案件：證明 Location 指向的網址真的能用
        var details = await _officerClient.GetFromJsonAsync<CaseDetails>(location, cancellationToken);
        Assert.NotNull(details);
        Assert.Equal(submitted.Id, details.Id);
        Assert.Equal("Closed", details.Status);
        Assert.Equal(TestTokens.OfficerDepartmentId, details.DepartmentId);
        Assert.Equal("路燈不亮", details.Subject);
        Assert.Equal("中山路路燈故障", details.Content);
        var supplement = Assert.Single(details.SupplementRequests);
        Assert.Equal("缺照片", supplement.Reason);
        Assert.NotNull(supplement.SubmittedAt);
    }

    [Fact]
    public async Task RejectCase_WithReason_ReturnsReasonInDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitted = await _citizenClient.SubmitCaseAsync("水管破裂", "民生路水管破裂");
        await _dispatcherClient.AssignAsync(submitted.Id, TestTokens.OfficerDepartmentId);

        await AssertNoContentAsync(_officerClient.PostAsJsonAsync(
            $"api/cases/{submitted.Id}/reject", new { reason = "非本單位業務" }, cancellationToken));

        var details = await _officerClient.GetFromJsonAsync<CaseDetails>($"api/cases/{submitted.Id}", cancellationToken);
        Assert.NotNull(details);
        Assert.Equal("Rejected", details.Status);
        Assert.Equal("非本單位業務", details.RejectionReason);
    }

    // 業務規則錯誤 → 409，並帶出目前狀態與目標狀態，讓前端能顯示原因
    [Fact]
    public async Task ReviewCase_WhenClosed_ReturnsConflictWithStatuses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitted = await _citizenClient.SubmitCaseAsync();
        await _dispatcherClient.AssignAsync(submitted.Id, TestTokens.OfficerDepartmentId);
        await AssertNoContentAsync(PostActionAsync(_officerClient, submitted.Id, "review"));
        await AssertNoContentAsync(PostActionAsync(_officerClient, submitted.Id, "close"));

        var response = await PostActionAsync(_officerClient, submitted.Id, "review");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("Closed", problem.RootElement.GetProperty("currentStatus").GetString());
        Assert.Equal("UnderReview", problem.RootElement.GetProperty("targetStatus").GetString());
    }

    // 陳情人資料是個資：查詢時只回傳遮罩後的值，完整的值不能出現在回應的任何地方
    [Fact]
    public async Task GetCase_WithPetitioner_ReturnsMaskedPetitioner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitResponse = await _citizenClient.PostAsJsonAsync("api/cases",
            new
            {
                subject = "路燈不亮",
                content = "中山路路燈故障",
                petitioner = new { name = "王小明", phone = "0912345678", email = "test@example.com" },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<SubmitCaseResult>(cancellationToken);
        Assert.NotNull(submitted);
        // 承辦人只看得到本單位的案件，要先分派
        await _dispatcherClient.AssignAsync(submitted.Id, TestTokens.OfficerDepartmentId);

        var response = await _officerClient.GetAsync(submitResponse.Headers.Location, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(body);
        var petitioner = json.RootElement.GetProperty("petitioner");
        Assert.Equal("王*明", petitioner.GetProperty("name").GetString());
        Assert.Equal("09******78", petitioner.GetProperty("phone").GetString());
        Assert.Equal("test********.com", petitioner.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, petitioner.GetProperty("address").ValueKind);

        Assert.DoesNotContain("王小明", body);
        Assert.DoesNotContain("0912345678", body);
        Assert.DoesNotContain("test@example.com", body);
    }

    // 只檢查 404 不夠：路由對不到時也是 404（但內容是空的），要確認是 NotFoundExceptionHandler 回的
    [Fact]
    public async Task GetCase_WithNonexistentId_ReturnsNotFoundProblem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await _officerClient.GetAsync($"api/cases/{Guid.NewGuid()}", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SubmitCase_WithBlankSubject_ReturnsValidationProblemForSubject()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await _citizenClient.PostAsJsonAsync("api/cases",
            new { subject = "", content = "中山路路燈故障", petitioner = ValidPetitioner },
            cancellationToken);

        await AssertValidationProblemAsync(response, "Subject");
    }

    // 「電話與 Email 至少一項」是 Domain 的規則（DomainValidationException），要回 400 而不是 409
    [Fact]
    public async Task SubmitCase_WithoutContact_ReturnsBadRequestWithDetail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await _citizenClient.PostAsJsonAsync("api/cases",
            new { subject = "路燈不亮", content = "中山路路燈故障", petitioner = new { name = "王小明", phone = "  " } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("資料不符合規定", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("電話與 Email 至少需填寫一項", problem.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task RejectCase_WithBlankReason_ReturnsValidationProblemForReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitted = await _citizenClient.SubmitCaseAsync("水管破裂", "民生路水管破裂");

        var response = await _officerClient.PostAsJsonAsync(
            $"api/cases/{submitted.Id}/reject", new { reason = "" }, cancellationToken);

        await AssertValidationProblemAsync(response, "Reason");
    }

    // ── Helpers ──────────────────────────────────────────

    // 不帶內容的動作端點（受理、補件、結案）。由誰執行，就傳入誰的 client
    private static Task<HttpResponseMessage> PostActionAsync(HttpClient client, Guid caseId, string action)
    {
        return client.PostAsync($"api/cases/{caseId}/{action}", null, TestContext.Current.CancellationToken);
    }

    private static async Task AssertNoContentAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
