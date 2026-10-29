using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Cases;
using CaseTrack.Application.Common;
using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure.Persistence.Configurations;
using static CaseTrack.IntegrationTests.Api.ApiTestHelpers;

namespace CaseTrack.IntegrationTests.Api;

// 主管核定結案：承辦人送出（/close）只會變成待核定，主管核定（/closing-approval）才結案，
// 主管也可以退回（/closing-return）。民眾在核定前看到的是「審核中」，看不到結案回覆
[Collection(nameof(DatabaseCollection))]
public class ClosingApprovalApiTests : IClassFixture<CaseTrackApiFactory>
{
    private readonly CaseTrackApiFactory _factory;
    private readonly HttpClient _citizenClient;
    private readonly HttpClient _dispatcherClient;
    private readonly HttpClient _officerClient;
    private readonly HttpClient _supervisorClient;

    public ClosingApprovalApiTests(CaseTrackApiFactory factory)
    {
        _factory = factory;
        _citizenClient = factory.CreateCitizenClient();
        _dispatcherClient = factory.CreateDispatcherClient();
        _officerClient = factory.CreateOfficerClient();
        _supervisorClient = factory.CreateSupervisorClient();
    }

    [Fact]
    public async Task Close_ByOfficer_IsPendingApprovalAndHiddenFromCitizen()
    {
        var caseId = await SubmitForClosingAsync(_officerClient, "已派員修復路燈");

        var staff = await GetStaffDetailsAsync(_supervisorClient, caseId);
        Assert.Equal(CaseStatus.PendingApproval, staff.Status);
        Assert.Equal("已派員修復路燈", staff.ClosingReply); // 主管要看得到才能核定
        Assert.Equal([CaseAction.ViewPetitioner, CaseAction.ApproveClosing, CaseAction.ReturnClosing],
            staff.AllowedActions);

        // 民眾：內部的簽核流程顯示成審核中，回覆還沒核定，不能先給民眾看
        var citizen = await GetCitizenDetailsAsync(caseId);
        Assert.Equal(CaseStatus.UnderReview, citizen.Status);
        Assert.Null(citizen.ClosingReply);
        var listed = await _citizenClient.GetFromJsonAsync<PagedResult<CaseSummary>>(
            "api/my/cases?pageSize=100", JsonOptions, TestContext.Current.CancellationToken);
        Assert.Equal(CaseStatus.UnderReview, Assert.Single(listed!.Items, c => c.Id == caseId).Status);
    }

    [Fact]
    public async Task ApproveClosing_BySupervisor_ClosesAndCitizenSeesReply()
    {
        var caseId = await SubmitForClosingAsync(_officerClient, "已派員修復路燈");

        await AssertNoContentAsync(PostAsync(_supervisorClient, caseId, "closing-approval"));

        Assert.Equal(CaseStatus.Closed, (await GetStaffDetailsAsync(_officerClient, caseId)).Status);
        var citizen = await GetCitizenDetailsAsync(caseId);
        Assert.Equal(CaseStatus.Closed, citizen.Status);
        Assert.Equal("已派員修復路燈", citizen.ClosingReply);
    }

    // 承辦人不能核定（Policy 只有主管）
    [Fact]
    public async Task ApproveClosing_ByOfficer_ReturnsForbidden()
    {
        var caseId = await SubmitForClosingAsync(_officerClient, "已派員修復路燈");

        var response = await PostAsync(_officerClient, caseId, "closing-approval");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // 職責分離：主管自己送出的結案，自己不能核定（409），畫面上也不會有核定按鈕
    [Fact]
    public async Task ApproveClosing_OwnSubmission_ReturnsConflictAndIsNotAllowed()
    {
        var caseId = await SubmitForClosingAsync(_supervisorClient, "主管自己承辦的案件");

        var details = await GetStaffDetailsAsync(_supervisorClient, caseId);
        Assert.DoesNotContain(CaseAction.ApproveClosing, details.AllowedActions);

        var response = await PostAsync(_supervisorClient, caseId, "closing-approval");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(CaseStatus.PendingApproval, (await GetStaffDetailsAsync(_supervisorClient, caseId)).Status);

        // 另一位主管可以核定
        var otherSupervisor = _factory.CreateSupervisorClient("u-supervisor-2");
        await AssertNoContentAsync(PostAsync(otherSupervisor, caseId, "closing-approval"));
    }

    // 別單位的主管看不到這件案件：404，不透露案件存在
    [Fact]
    public async Task ApproveClosing_SupervisorOfOtherDepartment_ReturnsNotFound()
    {
        var caseId = await SubmitForClosingAsync(_officerClient, "已派員修復路燈");
        var otherDepartment = _factory.CreateClientFor(
            "u-supervisor-tra", [Roles.Supervisor], DepartmentSeed.Transportation);

        var response = await PostAsync(otherDepartment, caseId, "closing-approval");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnClosing_BackToUnderReview_OfficerSeesReasonAndCanResubmit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var caseId = await SubmitForClosingAsync(_officerClient, "已處理");

        await AssertNoContentAsync(_supervisorClient.PostAsJsonAsync(
            $"api/cases/{caseId}/closing-return", new { reason = "請寫明施工日期" }, cancellationToken));

        var details = await GetStaffDetailsAsync(_officerClient, caseId);
        Assert.Equal(CaseStatus.UnderReview, details.Status);
        Assert.Equal("請寫明施工日期", details.ClosingReturnReason);
        Assert.Null(details.ClosingReply);
        Assert.Contains(CaseAction.Close, details.AllowedActions);

        await AssertNoContentAsync(_officerClient.PostAsJsonAsync(
            $"api/cases/{caseId}/close", new { reply = "已於 10/30 更換燈泡" }, cancellationToken));
        var resubmitted = await GetStaffDetailsAsync(_officerClient, caseId);
        Assert.Equal(CaseStatus.PendingApproval, resubmitted.Status);
        Assert.Null(resubmitted.ClosingReturnReason);
    }

    [Fact]
    public async Task ReturnClosing_WithBlankReason_ReturnsValidationProblem()
    {
        var caseId = await SubmitForClosingAsync(_officerClient, "已處理");

        var response = await _supervisorClient.PostAsJsonAsync(
            $"api/cases/{caseId}/closing-return", new { reason = "" }, TestContext.Current.CancellationToken);

        await AssertValidationProblemAsync(response, "Reason", "退回原因不可為空");
    }

    // 還沒送出結案（審核中）就核定：狀態不對 → 409，帶出目前狀態
    [Fact]
    public async Task ApproveClosing_WhenUnderReview_ReturnsConflict()
    {
        var caseId = await UnderReviewAsync();

        var response = await PostAsync(_supervisorClient, caseId, "closing-approval");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("UnderReview", problem.RootElement.GetProperty("currentStatus").GetString());
    }

    // ── Helpers ──────────────────────────────────────────

    private async Task<Guid> UnderReviewAsync()
    {
        var submitted = await _citizenClient.SubmitCaseAsync();
        await _dispatcherClient.AssignAsync(submitted.Id, TestTokens.OfficerDepartmentId);
        await AssertNoContentAsync(PostAsync(_officerClient, submitted.Id, "review"));
        return submitted.Id;
    }

    // submitter：誰送出結案（承辦人或主管自己）
    private async Task<Guid> SubmitForClosingAsync(HttpClient submitter, string reply)
    {
        var caseId = await UnderReviewAsync();
        await AssertNoContentAsync(submitter.PostAsJsonAsync(
            $"api/cases/{caseId}/close", new { reply }, TestContext.Current.CancellationToken));
        return caseId;
    }

    private static async Task<CaseDetails> GetStaffDetailsAsync(HttpClient client, Guid caseId) =>
        (await client.GetFromJsonAsync<CaseDetails>(
            $"api/cases/{caseId}", JsonOptions, TestContext.Current.CancellationToken))!;

    private async Task<CitizenCaseDetails> GetCitizenDetailsAsync(Guid caseId) =>
        (await _citizenClient.GetFromJsonAsync<CitizenCaseDetails>(
            $"api/my/cases/{caseId}", JsonOptions, TestContext.Current.CancellationToken))!;

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid caseId, string action) =>
        client.PostAsync($"api/cases/{caseId}/{action}", null, TestContext.Current.CancellationToken);
}
