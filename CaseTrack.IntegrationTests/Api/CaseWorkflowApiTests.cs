using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CaseTrack.Application.Cases;

namespace CaseTrack.IntegrationTests.Api;

// 透過 HTTP 測整個 API：路由、模型繫結、驗證、例外處理、JSON 序列化都會經過。
// 站在外部使用者的角度：請求用匿名型別，狀態比對字串（API 的合約），不依賴 Domain 的型別。
[Collection(nameof(DatabaseCollection))]
public class CaseWorkflowApiTests : IClassFixture<CaseTrackApiFactory>
{
    private const string ProblemJson = "application/problem+json";

    private readonly HttpClient _client;

    public CaseWorkflowApiTests(CaseTrackApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FullWorkflow_FromSubmitToClose_EndsClosed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // 提交：201 + Location
        var submitResponse = await _client.PostAsJsonAsync("api/cases",
            new { subject = "路燈不亮", content = "中山路路燈故障" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<SubmitCaseResult>(cancellationToken);
        Assert.NotNull(submitted);
        Assert.Matches(@"^\d{8}-\d{4}$", submitted.CaseNumber);
        var location = submitResponse.Headers.Location;
        Assert.NotNull(location);

        // 處理流程：每一步都依賴前一步真的生效，任何一步沒生效，後面就會 409
        await AssertNoContentAsync(PostAsync(submitted.Id, "review"));
        await AssertNoContentAsync(_client.PostAsJsonAsync(
            $"api/cases/{submitted.Id}/supplement-requests", new { reason = "缺照片" }, cancellationToken));
        await AssertNoContentAsync(PostAsync(submitted.Id, "supplement-submission"));
        await AssertNoContentAsync(PostAsync(submitted.Id, "close"));

        // 用 Location 取回案件：證明 Location 指向的網址真的能用
        var details = await _client.GetFromJsonAsync<CaseDetails>(location, cancellationToken);
        Assert.NotNull(details);
        Assert.Equal(submitted.Id, details.Id);
        Assert.Equal("Closed", details.Status);
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
        var submitted = await SubmitCaseAsync("水管破裂", "民生路水管破裂");

        await AssertNoContentAsync(_client.PostAsJsonAsync(
            $"api/cases/{submitted.Id}/reject", new { reason = "非本單位業務" }, cancellationToken));

        var details = await _client.GetFromJsonAsync<CaseDetails>($"api/cases/{submitted.Id}", cancellationToken);
        Assert.NotNull(details);
        Assert.Equal("Rejected", details.Status);
        Assert.Equal("非本單位業務", details.RejectionReason);
    }

    // 業務規則錯誤 → 409，並帶出目前狀態與目標狀態，讓前端能顯示原因
    [Fact]
    public async Task ReviewCase_WhenClosed_ReturnsConflictWithStatuses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitted = await SubmitCaseAsync("路燈不亮", "中山路路燈故障");
        await AssertNoContentAsync(PostAsync(submitted.Id, "review"));
        await AssertNoContentAsync(PostAsync(submitted.Id, "close"));

        var response = await PostAsync(submitted.Id, "review");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("Closed", problem.RootElement.GetProperty("currentStatus").GetString());
        Assert.Equal("UnderReview", problem.RootElement.GetProperty("targetStatus").GetString());
    }

    // 只檢查 404 不夠：路由對不到時也是 404（但內容是空的），要確認是 NotFoundExceptionHandler 回的
    [Fact]
    public async Task GetCase_WithNonexistentId_ReturnsNotFoundProblem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync($"api/cases/{Guid.NewGuid()}", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SubmitCase_WithBlankSubject_ReturnsValidationProblemForSubject()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync("api/cases",
            new { subject = "", content = "中山路路燈故障" },
            cancellationToken);

        await AssertValidationProblemAsync(response, "Subject");
    }

    [Fact]
    public async Task RejectCase_WithBlankReason_ReturnsValidationProblemForReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitted = await SubmitCaseAsync("水管破裂", "民生路水管破裂");

        var response = await _client.PostAsJsonAsync(
            $"api/cases/{submitted.Id}/reject", new { reason = "" }, cancellationToken);

        await AssertValidationProblemAsync(response, "Reason");
    }

    // ── Helpers ──────────────────────────────────────────

    private async Task<SubmitCaseResult> SubmitCaseAsync(string subject, string content)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync("api/cases", new { subject, content }, cancellationToken);

        // 準備資料失敗時，直接在這裡失敗，錯誤訊息才看得出是哪一步出問題
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SubmitCaseResult>(cancellationToken);
        Assert.NotNull(result);
        return result;
    }

    // 不帶內容的動作端點（受理、補件、結案）
    private Task<HttpResponseMessage> PostAsync(Guid caseId, string action)
    {
        return _client.PostAsync($"api/cases/{caseId}/{action}", null, TestContext.Current.CancellationToken);
    }

    private static async Task AssertNoContentAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // 驗證失敗 → 400 ProblemDetails，而且 errors 裡指出是哪個欄位
    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _),
            $"errors 裡沒有 {field}：{problem.RootElement}");
    }
}
