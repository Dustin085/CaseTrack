using System.Net;
using System.Net.Http.Json;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Cases;
using CaseTrack.Infrastructure.Persistence;
using CaseTrack.Infrastructure.Persistence.Auditing;
using CaseTrack.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CaseTrack.IntegrationTests.Api;

// 陳情人的完整個資：預設遮罩，承辦人明確要求時才顯示，而且每次都留下查詢紀錄
[Collection(nameof(DatabaseCollection))]
public class PersonalDataApiTests : IClassFixture<CaseTrackApiFactory>
{
    private readonly CaseTrackApiFactory _factory;
    private readonly HttpClient _citizenClient;
    private readonly HttpClient _dispatcherClient;
    private readonly HttpClient _officerClient;

    public PersonalDataApiTests(CaseTrackApiFactory factory)
    {
        _factory = factory;
        _citizenClient = factory.CreateCitizenClient();
        _dispatcherClient = factory.CreateDispatcherClient();
        _officerClient = factory.CreateOfficerClient();
    }

    [Fact]
    public async Task GetPetitioner_ByOfficerOfAssignedDepartment_ReturnsFullDataAndRecordsAccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        var response = await GetPetitionerAsync(_officerClient, caseId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var petitioner = await response.Content.ReadFromJsonAsync<PetitionerDetails>(cancellationToken);
        Assert.NotNull(petitioner);
        Assert.Equal("王小明", petitioner.Name);
        Assert.Equal("0912345678", petitioner.Phone);

        // 個資不能被瀏覽器或中間的 proxy 快取
        Assert.True(response.Headers.CacheControl?.NoStore, "完整個資的回應必須是 Cache-Control: no-store");

        var entry = Assert.Single(await GetAccessEntriesAsync(caseId));
        Assert.Equal(TestTokens.OfficerUserId, entry.AccessedBy);
    }

    // 預設的查詢是遮罩後的資料，沒有看到完整個資，就不留查詢紀錄（紀錄才有意義）
    [Fact]
    public async Task GetCase_ByOfficer_DoesNotRecordAccess()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        var response = await _officerClient.GetAsync($"api/cases/{caseId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await GetAccessEntriesAsync(caseId));
    }

    // 查詢紀錄本身不再寫進稽核紀錄：只能新增的紀錄，再記一份只是重複。
    // 不能用 EntityId 找：查詢紀錄的主鍵是 IDENTITY，攔截器在存檔「之前」執行，拿到的是 EF 的暫時值（負數），
    // 跟存檔後的 Id 對不上，用 Id 找永遠是 0 筆，測試會因為錯誤的原因通過。所以改用 Changes 裡的案件 Id 找
    [Fact]
    public async Task GetPetitioner_RecordsAccess_WithoutAuditEntry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        await GetPetitionerAsync(_officerClient, caseId);

        Assert.Single(await GetAccessEntriesAsync(caseId));
        using var scope = _factory.Services.CreateScope();
        var auditedAccessEntries = await scope.ServiceProvider.GetRequiredService<CaseTrackDbContext>().AuditEntries
            .Where(a => a.EntityType == nameof(PersonalDataAccessEntry) && a.Changes.Contains(caseId.ToString()))
            .CountAsync(cancellationToken);
        Assert.Equal(0, auditedAccessEntries);
    }

    // 分派人員看得到案件，但不需要知道民眾是誰
    [Fact]
    public async Task GetPetitioner_ByDispatcher_ReturnsForbiddenWithoutRecord()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        var response = await GetPetitionerAsync(_dispatcherClient, caseId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await GetAccessEntriesAsync(caseId));
    }

    [Fact]
    public async Task GetPetitioner_ByAdministrator_ReturnsForbidden()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();
        var administratorClient = _factory.CreateClientFor("u-admin-1", [Roles.Administrator], DepartmentSeed.PublicWorks);

        var response = await GetPetitionerAsync(administratorClient, caseId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPetitioner_OfficerOfOtherDepartment_ReturnsNotFoundWithoutRecord()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();
        var otherOfficerClient = _factory.CreateClientFor("u-officer-tra", [Roles.CaseOfficer], DepartmentSeed.Transportation);

        var response = await GetPetitionerAsync(otherOfficerClient, caseId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await GetAccessEntriesAsync(caseId));
    }

    [Fact]
    public async Task GetPetitioner_WithoutToken_ReturnsUnauthorized()
    {
        var caseId = await SubmitAndAssignToPublicWorksAsync();

        var response = await GetPetitionerAsync(_citizenClient, caseId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────

    private async Task<Guid> SubmitAndAssignToPublicWorksAsync()
    {
        var submitted = await _citizenClient.SubmitCaseAsync();
        await _dispatcherClient.AssignAsync(submitted.Id, DepartmentSeed.PublicWorks);
        return submitted.Id;
    }

    private static Task<HttpResponseMessage> GetPetitionerAsync(HttpClient client, Guid caseId) =>
        client.GetAsync($"api/cases/{caseId}/petitioner", TestContext.Current.CancellationToken);

    private async Task<List<PersonalDataAccessEntry>> GetAccessEntriesAsync(Guid caseId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CaseTrackDbContext>().PersonalDataAccessEntries
            .Where(e => e.CaseId == caseId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
