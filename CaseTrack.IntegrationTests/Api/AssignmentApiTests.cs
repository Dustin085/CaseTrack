using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CaseTrack.Application.Cases;
using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure.Persistence.Configurations;
using static CaseTrack.IntegrationTests.Api.ApiTestHelpers;

namespace CaseTrack.IntegrationTests.Api;

// 分派：只有分派人員可以做，而且每一種錯誤都要回對的狀態碼
[Collection(nameof(DatabaseCollection))]
public class AssignmentApiTests : IClassFixture<CaseTrackApiFactory>
{
    private readonly HttpClient _citizenClient;
    private readonly HttpClient _dispatcherClient;
    private readonly HttpClient _officerClient;

    public AssignmentApiTests(CaseTrackApiFactory factory)
    {
        _citizenClient = factory.CreateCitizenClient();
        _dispatcherClient = factory.CreateDispatcherClient();
        _officerClient = factory.CreateOfficerClient();
    }

    [Fact]
    public async Task AssignCase_ByDispatcher_SetsDepartment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitted = await _citizenClient.SubmitCaseAsync();

        var response = await _dispatcherClient.PostAssignmentAsync(submitted.Id, DepartmentSeed.Transportation);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // 用分派人員查詢：分派到交通局之後，工務局的承辦人已經看不到這個案件了
        var details = await _dispatcherClient.GetFromJsonAsync<CaseDetails>($"api/cases/{submitted.Id}", JsonOptions, cancellationToken);
        Assert.NotNull(details);
        Assert.Equal(DepartmentSeed.Transportation, details.DepartmentId);
        Assert.Equal(CaseStatus.Submitted, details.Status);
    }

    // 角色不對：在讀取案件之前就被擋下，不管案件存不存在都是 403，所以不會洩漏案件是否存在
    [Fact]
    public async Task AssignCase_ByOfficer_ReturnsForbidden()
    {
        var submitted = await _citizenClient.SubmitCaseAsync();

        var response = await _officerClient.PostAssignmentAsync(submitted.Id, DepartmentSeed.PublicWorks);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AssignCase_WithoutToken_ReturnsUnauthorized()
    {
        var submitted = await _citizenClient.SubmitCaseAsync();

        var response = await _citizenClient.PostAssignmentAsync(submitted.Id, DepartmentSeed.PublicWorks);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 單位在請求內容裡，不存在是「送來的資料有問題」→ 400，而且跟沒填一樣指出 DepartmentId 欄位
    [Fact]
    public async Task AssignCase_ToNonexistentDepartment_ReturnsValidationProblemForDepartmentId()
    {
        var submitted = await _citizenClient.SubmitCaseAsync();

        var response = await _dispatcherClient.PostAssignmentAsync(submitted.Id, Guid.NewGuid());

        await AssertValidationProblemAsync(response, "DepartmentId", "單位不存在");
    }

    // DTO 用 Guid? + [Required]：寫成 Guid 的話，沒填會變成 Guid.Empty 繞過 [Required]，
    // 雖然 Handler 查不到單位也會回 400，但訊息會變成「單位不存在」，使用者會以為是選錯了單位
    [Fact]
    public async Task AssignCase_WithoutDepartmentId_ReturnsValidationProblemForDepartmentId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitted = await _citizenClient.SubmitCaseAsync();

        var response = await _dispatcherClient.PostAsJsonAsync(
            $"api/cases/{submitted.Id}/assignment", new { }, cancellationToken);

        await AssertValidationProblemAsync(response, "DepartmentId", "單位不可為空");
    }

    [Fact]
    public async Task AssignCase_NonexistentCase_ReturnsNotFound()
    {
        var response = await _dispatcherClient.PostAssignmentAsync(Guid.NewGuid(), DepartmentSeed.PublicWorks);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    // 受理之後由那個單位負責，不能再分派（要換單位是「轉派」）
    [Fact]
    public async Task AssignCase_AfterReview_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitted = await _citizenClient.SubmitCaseAsync();
        await _dispatcherClient.AssignAsync(submitted.Id, DepartmentSeed.PublicWorks);
        var reviewResponse = await _officerClient.PostAsync($"api/cases/{submitted.Id}/review", null, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, reviewResponse.StatusCode);

        var response = await _dispatcherClient.PostAssignmentAsync(submitted.Id, DepartmentSeed.Transportation);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("只有已送出的案件可以分派，目前狀態：UnderReview", problem.RootElement.GetProperty("detail").GetString());
    }

    // 未分派的案件不屬於任何單位，承辦人看不到，所以是 404，走不到 Domain 的「尚未分派」（409）。
    // Domain 的規則仍然保留：它是最後一道防線，不依賴 API 的權限設定是否正確
    [Fact]
    public async Task ReviewCase_NotAssigned_ReturnsNotFoundForOfficer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitted = await _citizenClient.SubmitCaseAsync();

        var response = await _officerClient.PostAsync($"api/cases/{submitted.Id}/review", null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
