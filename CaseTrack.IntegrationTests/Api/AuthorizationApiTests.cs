using System.Net;
using System.Net.Http.Json;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Cases;
using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure.Persistence.Configurations;
using static CaseTrack.IntegrationTests.Api.ApiTestHelpers;

namespace CaseTrack.IntegrationTests.Api;

// 授權的兩層：
//   第 1 層（角色，Policy）：這個角色能不能做這個動作 → 403
//   第 2 層（資料，CaseAccessScope）：能不能碰這一筆案件 → 404（不洩漏案件是否存在）
[Collection(nameof(DatabaseCollection))]
public class AuthorizationApiTests : IClassFixture<CaseTrackApiFactory>
{
    private readonly HttpClient _citizenClient;
    private readonly HttpClient _dispatcherClient;
    private readonly HttpClient _officerClient;                  // 工務局承辦人
    private readonly HttpClient _otherDepartmentOfficerClient;   // 交通局承辦人
    private readonly HttpClient _supervisorClient;               // 工務局主管
    private readonly HttpClient _administratorClient;

    public AuthorizationApiTests(CaseTrackApiFactory factory)
    {
        _citizenClient = factory.CreateCitizenClient();
        _dispatcherClient = factory.CreateDispatcherClient();
        _officerClient = factory.CreateOfficerClient();
        _otherDepartmentOfficerClient = factory.CreateClientFor(
            "u-officer-tra", [Roles.CaseOfficer], DepartmentSeed.Transportation);
        _supervisorClient = factory.CreateClientFor(
            "u-supervisor-1", [Roles.Supervisor], DepartmentSeed.PublicWorks);
        // 管理者就算有單位也不能看案件：給他單位，才能證明擋下他的不是「沒有單位」
        _administratorClient = factory.CreateClientFor(
            "u-admin-1", [Roles.Administrator], DepartmentSeed.PublicWorks);
    }

    // ── 第 2 層：資料（404）──────────────────────────────

    // 正面對照：本單位的承辦人看得到。沒有這個測試，下面的 404 可能只是因為「什麼都查不到」
    [Fact]
    public async Task GetCase_OfficerOfAssignedDepartment_ReturnsOk()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        var response = await GetCaseAsync(_officerClient, caseId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCase_OfficerOfOtherDepartment_ReturnsNotFound()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        var response = await GetCaseAsync(_otherDepartmentOfficerClient, caseId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReviewCase_OfficerOfOtherDepartment_ReturnsNotFound()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        var response = await PostActionAsync(_otherDepartmentOfficerClient, caseId, "review");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCase_UnassignedByOfficer_ReturnsNotFound()
    {
        var submitted = await _citizenClient.SubmitCaseAsync();

        var response = await GetCaseAsync(_officerClient, submitted.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // 分派人員要看得到未分派的案件，才有辦法分派
    [Fact]
    public async Task GetCase_UnassignedByDispatcher_ReturnsOk()
    {
        var submitted = await _citizenClient.SubmitCaseAsync();

        var response = await GetCaseAsync(_dispatcherClient, submitted.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // 主管跟承辦人一樣，可以處理本單位的案件
    [Fact]
    public async Task ReviewCase_SupervisorOfAssignedDepartment_ReturnsNoContent()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        var response = await PostActionAsync(_supervisorClient, caseId, "review");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── 第 1 層：角色（403）──────────────────────────────

    // 分派人員看得到所有案件（範圍是全部），擋下他的必須是角色，不能靠範圍
    [Fact]
    public async Task ReviewCase_ByDispatcher_ReturnsForbidden()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        var response = await PostActionAsync(_dispatcherClient, caseId, "review");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // 分派人員要拒絕案件，用的是「不予受理」，不是承辦單位的「退件」
    [Fact]
    public async Task RejectCase_ByDispatcher_ReturnsForbidden()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        var response = await _dispatcherClient.PostAsJsonAsync(
            $"api/cases/{caseId}/reject", new { reason = "非本府權責" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // 最小權限：管理者維護系統設定，沒有業務需要看民眾的個資
    [Fact]
    public async Task GetCase_ByAdministrator_ReturnsForbidden()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        var response = await GetCaseAsync(_administratorClient, caseId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 不予受理 ─────────────────────────────────────────

    [Fact]
    public async Task DismissCase_ByDispatcher_RejectsWithReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitted = await _citizenClient.SubmitCaseAsync();

        var response = await DismissAsync(_dispatcherClient, submitted.Id, "非本府權責");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var details = await _dispatcherClient.GetFromJsonAsync<CaseDetails>($"api/cases/{submitted.Id}", JsonOptions, cancellationToken);
        Assert.NotNull(details);
        Assert.Equal(CaseStatus.Rejected, details.Status);
        Assert.Equal("非本府權責", details.RejectionReason);
    }

    [Fact]
    public async Task DismissCase_ByOfficer_ReturnsForbidden()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        var response = await DismissAsync(_officerClient, caseId, "非本府權責");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // 受理之後就不能不予受理：這是 Domain 的狀態規則（409），不是權限（403）
    [Fact]
    public async Task DismissCase_AfterReview_ReturnsConflict()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();
        var reviewResponse = await PostActionAsync(_officerClient, caseId, "review");
        Assert.Equal(HttpStatusCode.NoContent, reviewResponse.StatusCode);

        var response = await DismissAsync(_dispatcherClient, caseId, "非本府權責");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DismissCase_WithBlankReason_ReturnsValidationProblemForReason()
    {
        var submitted = await _citizenClient.SubmitCaseAsync();

        var response = await DismissAsync(_dispatcherClient, submitted.Id, "");

        await AssertValidationProblemAsync(response, "Reason", "不予受理的原因不可為空");
    }

    // ── Helpers ──────────────────────────────────────────

    private async Task<Guid> SubmitAndAssignToPublicWorksAsync()
    {
        var submitted = await _citizenClient.SubmitCaseAsync();
        await _dispatcherClient.AssignAsync(submitted.Id, DepartmentSeed.PublicWorks);
        return submitted.Id;
    }

    private static Task<HttpResponseMessage> GetCaseAsync(HttpClient client, Guid caseId) =>
        client.GetAsync($"api/cases/{caseId}", TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> PostActionAsync(HttpClient client, Guid caseId, string action) =>
        client.PostAsync($"api/cases/{caseId}/{action}", null, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> DismissAsync(HttpClient client, Guid caseId, string reason) =>
        client.PostAsJsonAsync($"api/cases/{caseId}/dismissal", new { reason }, TestContext.Current.CancellationToken);
}
