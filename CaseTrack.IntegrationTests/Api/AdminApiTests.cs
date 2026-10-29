using System.Net;
using System.Net.Http.Json;
using CaseTrack.Application.Auditing;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Common;
using CaseTrack.Application.Departments;
using CaseTrack.Infrastructure.Persistence.Configurations;
using static CaseTrack.IntegrationTests.Api.ApiTestHelpers;

namespace CaseTrack.IntegrationTests.Api;

// 管理者：單位維護（新增、改名、停用、啟用）與稽核紀錄查詢。
// 測試都用自己新增的單位，不停用種子資料的單位：資料庫是所有測試共用的，停用工務局會讓其他測試的分派失敗
[Collection(nameof(DatabaseCollection))]
public class AdminApiTests : IClassFixture<CaseTrackApiFactory>
{
    private const string AdminUserId = "u-admin-1";

    private readonly CaseTrackApiFactory _factory;
    private readonly HttpClient _adminClient;

    public AdminApiTests(CaseTrackApiFactory factory)
    {
        _factory = factory;
        _adminClient = factory.CreateClientFor(AdminUserId, [Roles.Administrator]);
    }

    [Fact]
    public async Task CreateDepartment_NormalizesCodeAndIsListedAsActive()
    {
        var code = NewCode();

        var created = await CreateDepartmentAsync(code.ToLowerInvariant(), " 測試局 ");

        Assert.Equal(code, created.Code);
        Assert.Equal("測試局", created.Name);
        Assert.True(created.IsActive);
        Assert.Contains(await ListDepartmentsAsync(_factory.CreateDispatcherClient()), d => d.Id == created.Id);
    }

    [Fact]
    public async Task CreateDepartment_DuplicateCode_ReturnsValidationProblem()
    {
        var code = NewCode();
        await CreateDepartmentAsync(code, "測試局");

        var response = await PostDepartmentAsync(code.ToLowerInvariant(), "另一個測試局");

        await AssertValidationProblemAsync(response, "Code", $"代碼 {code} 已經存在");
    }

    [Theory]
    [InlineData("A-1")]
    [InlineData(" PWD ")]
    [InlineData("")]
    [InlineData("ABCDEFGHIJK")] // 11 個字元
    public async Task CreateDepartment_InvalidCode_ReturnsValidationProblem(string code)
    {
        var response = await PostDepartmentAsync(code, "測試局");

        await AssertValidationProblemAsync(response, "Code");
    }

    [Fact]
    public async Task RenameDepartment_ChangesNameAndIsAudited()
    {
        var created = await CreateDepartmentAsync(NewCode(), "舊名稱");

        var response = await _adminClient.PutAsJsonAsync($"api/admin/departments/{created.Id}/name",
            new { name = "新名稱" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("新名稱", (await ListDepartmentsAsync(_adminClient)).Single(d => d.Id == created.Id).Name);

        // 單位的欄位都在白名單上：管理者看得到改名的前後值，也看得到是誰改的
        var entries = await ListAuditEntriesAsync($"entityType=Department&entityId={created.Id}");
        var modified = Assert.Single(entries.Items, e => e.Action == "Modified");
        Assert.Equal(AdminUserId, modified.ChangedBy);
        var change = Assert.Single(modified.Changes);
        Assert.Equal(new AuditFieldChange("Name", "舊名稱", "新名稱", Hidden: false), change);
    }

    [Fact]
    public async Task RenameDepartment_Unknown_ReturnsNotFound()
    {
        var response = await _adminClient.PutAsJsonAsync($"api/admin/departments/{Guid.NewGuid()}/name",
            new { name = "新名稱" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // 停用後不能再分派，但仍然在單位清單上（舊案件要能顯示名稱）；重新啟用後又可以分派
    [Fact]
    public async Task DeactivateDepartment_BlocksAssignmentUntilActivated()
    {
        var department = await CreateDepartmentAsync(NewCode(), "即將裁撤局");
        var citizen = _factory.CreateCitizenClient();
        var dispatcher = _factory.CreateDispatcherClient();
        var submitted = await citizen.SubmitCaseAsync();

        await AssertNoContentAsync(_adminClient.PostAsync(
            $"api/admin/departments/{department.Id}/deactivation", null, TestContext.Current.CancellationToken));

        await AssertValidationProblemAsync(
            await dispatcher.PostAssignmentAsync(submitted.Id, department.Id), "DepartmentId", "單位不存在或已停用");
        Assert.False((await ListDepartmentsAsync(dispatcher)).Single(d => d.Id == department.Id).IsActive);

        await AssertNoContentAsync(_adminClient.PostAsync(
            $"api/admin/departments/{department.Id}/activation", null, TestContext.Current.CancellationToken));

        await dispatcher.AssignAsync(submitted.Id, department.Id);
    }

    // 管理者看不到案件內容：主旨、內容、陳情人等欄位只知道「有這個欄位」，值在 API 回應裡就不存在
    [Fact]
    public async Task ListAuditEntries_HidesCaseContent()
    {
        var subject = $"稽核測試主旨-{Guid.NewGuid():N}";
        var submitted = await _factory.CreateCitizenClient().SubmitCaseAsync(subject: subject);

        var response = await _adminClient.GetAsync(
            $"api/admin/audit-entries?entityType=Case&entityId={submitted.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(subject, body);
        Assert.DoesNotContain("王小明", body);

        var entries = await response.Content.ReadFromJsonAsync<PagedResult<AuditEntrySummary>>(
            JsonOptions, TestContext.Current.CancellationToken);
        var added = Assert.Single(entries!.Items);
        Assert.Equal("Added", added.Action);
        Assert.Equal(new AuditFieldChange("Subject", null, null, Hidden: true),
            added.Changes.Single(c => c.Field == "Subject"));
        Assert.True(added.Changes.Single(c => c.Field == "Petitioner.Name").Hidden);
        Assert.Equal(new AuditFieldChange("CaseNumber", null, submitted.CaseNumber, Hidden: false),
            added.Changes.Single(c => c.Field == "CaseNumber"));
        Assert.Equal("Submitted", added.Changes.Single(c => c.Field == "Status").New);
    }

    [Fact]
    public async Task ListAuditEntries_FromAfterTo_ReturnsValidationProblem()
    {
        var response = await _adminClient.GetAsync(
            "api/admin/audit-entries?from=2026-10-31&to=2026-10-01", TestContext.Current.CancellationToken);

        await AssertValidationProblemAsync(response, "From", "開始日期不能晚於結束日期");
    }

    // 管理者以外都不能用管理功能（包含主管）
    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.CaseOfficer)]
    [InlineData(Roles.Dispatcher)]
    [InlineData(Roles.Citizen)]
    public async Task AdminEndpoints_ByNonAdministrator_ReturnForbidden(string role)
    {
        var client = _factory.CreateClientFor("u-other-1", [role], DepartmentSeed.PublicWorks);
        var cancellationToken = TestContext.Current.CancellationToken;

        var list = await client.GetAsync("api/admin/departments", cancellationToken);
        var create = await client.PostAsJsonAsync("api/admin/departments",
            new { code = NewCode(), name = "測試局" }, cancellationToken);
        var deactivate = await client.PostAsync(
            $"api/admin/departments/{DepartmentSeed.PublicWorks}/deactivation", null, cancellationToken);
        var audit = await client.GetAsync("api/admin/audit-entries", cancellationToken);

        Assert.All([list, create, deactivate, audit], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    // 測試之間共用資料庫，代碼要每次不同（最多 10 個字元）
    private static string NewCode() => "T" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private Task<HttpResponseMessage> PostDepartmentAsync(string code, string name) =>
        _adminClient.PostAsJsonAsync("api/admin/departments", new { code, name }, TestContext.Current.CancellationToken);

    private async Task<DepartmentSummary> CreateDepartmentAsync(string code, string name)
    {
        var response = await PostDepartmentAsync(code, name);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var department = await response.Content.ReadFromJsonAsync<DepartmentSummary>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(department);
        return department;
    }

    private static async Task<IReadOnlyList<DepartmentSummary>> ListDepartmentsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<DepartmentSummary>>(
            "api/departments", JsonOptions, TestContext.Current.CancellationToken))!;

    private async Task<PagedResult<AuditEntrySummary>> ListAuditEntriesAsync(string query) =>
        (await _adminClient.GetFromJsonAsync<PagedResult<AuditEntrySummary>>(
            $"api/admin/audit-entries?{query}", JsonOptions, TestContext.Current.CancellationToken))!;
}
