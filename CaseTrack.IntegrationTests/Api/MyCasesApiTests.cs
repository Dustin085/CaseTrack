using System.Net;
using System.Net.Http.Json;
using CaseTrack.Application.Cases;
using CaseTrack.Application.Common;
using CaseTrack.Infrastructure.Persistence;
using CaseTrack.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CaseTrack.IntegrationTests.Api.ApiTestHelpers;

namespace CaseTrack.IntegrationTests.Api;

// 民眾端 /api/my/cases：要登入、只能是民眾，而且只碰得到自己送出的案件（別人的一律 404）
[Collection(nameof(DatabaseCollection))]
public class MyCasesApiTests : IClassFixture<CaseTrackApiFactory>
{
    private readonly CaseTrackApiFactory _factory;
    private readonly HttpClient _citizenA;
    private readonly HttpClient _citizenB;
    private readonly HttpClient _dispatcherClient;
    private readonly HttpClient _officerClient;

    public MyCasesApiTests(CaseTrackApiFactory factory)
    {
        _factory = factory;
        // 每個測試類別的實例用不同的民眾：資料庫是共用的，列表的總筆數才不會被其他測試的案件影響
        _citizenA = factory.CreateCitizenClient($"u-citizen-a-{Guid.NewGuid():N}");
        _citizenB = factory.CreateCitizenClient($"u-citizen-b-{Guid.NewGuid():N}");
        _dispatcherClient = factory.CreateDispatcherClient();
        _officerClient = factory.CreateOfficerClient();
    }

    // ── 驗證與角色 ───────────────────────────────────────

    [Fact]
    public async Task SubmitCase_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("api/my/cases",
            new { subject = "路燈不亮", content = "中山路路燈故障", petitioner = ValidPetitioner },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 承辦人、分派人員有登入，但不是民眾：403。承辦人自己要陳情，要用民眾的身分
    [Fact]
    public async Task ListMyCases_ByStaff_ReturnsForbidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var officerResponse = await _officerClient.GetAsync("api/my/cases", cancellationToken);
        var dispatcherResponse = await _dispatcherClient.GetAsync("api/my/cases", cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, officerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, dispatcherResponse.StatusCode);
    }

    // 民眾不能用承辦端的 API
    [Fact]
    public async Task GetCase_StaffEndpointByCitizen_ReturnsForbidden()
    {
        var submitted = await _citizenA.SubmitCaseAsync();

        var response = await _citizenA.GetAsync($"api/cases/{submitted.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 送件 ─────────────────────────────────────────────

    // 送件人來自 token，不是請求內容：之後「我的案件」靠它過濾
    [Fact]
    public async Task SubmitCase_RecordsSubmitterFromToken_AndLocationPointsToMyCase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = $"u-citizen-{Guid.NewGuid():N}";
        var client = _factory.CreateCitizenClient(userId);

        var response = await client.PostAsJsonAsync("api/my/cases",
            new { subject = "路燈不亮", content = "中山路路燈故障", petitioner = ValidPetitioner }, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var submitted = await response.Content.ReadFromJsonAsync<SubmitCaseResult>(cancellationToken);
        Assert.NotNull(submitted);
        Assert.Equal($"/api/my/cases/{submitted.Id}", response.Headers.Location?.AbsolutePath);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CaseTrackDbContext>();
        var submittedBy = await dbContext.Cases.Where(c => c.Id == submitted.Id).Select(c => c.SubmittedBy)
            .SingleAsync(cancellationToken);
        Assert.Equal(userId, submittedBy);
    }

    // ── 列表 ─────────────────────────────────────────────

    // B 的案件已分派：如果誤用了其他範圍，也會被列出來
    [Fact]
    public async Task ListMyCases_ReturnsOnlyOwnCases()
    {
        var mine1 = await _citizenA.SubmitCaseAsync();
        var mine2 = await _citizenA.SubmitCaseAsync();
        var others = await _citizenB.SubmitCaseAsync();
        await _dispatcherClient.AssignAsync(others.Id, DepartmentSeed.PublicWorks);

        var result = await _citizenA.GetFromJsonAsync<PagedResult<CaseSummary>>(
            "api/my/cases", JsonOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(
            new[] { mine1.Id, mine2.Id }.Order(),
            result.Items.Select(c => c.Id).Order());
    }

    // ── 詳情 ─────────────────────────────────────────────

    // 本人看自己的資料：不遮罩、回應不能被快取、也不寫個資查詢紀錄（查詢紀錄是給公務員查看別人個資用的）
    [Fact]
    public async Task GetMyCase_ReturnsFullPetitionerWithNoStore_WithoutAccessRecord()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitted = await _citizenA.SubmitCaseAsync();

        var response = await _citizenA.GetAsync($"api/my/cases/{submitted.Id}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore, "回應包含個資，必須是 no-store");
        var details = await response.Content.ReadFromJsonAsync<CitizenCaseDetails>(JsonOptions, cancellationToken);
        Assert.NotNull(details);
        Assert.Equal("王小明", details.Petitioner.Name);
        Assert.Equal("0912345678", details.Petitioner.Phone);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CaseTrackDbContext>();
        Assert.False(await dbContext.PersonalDataAccessEntries.AnyAsync(e => e.CaseId == submitted.Id, cancellationToken));
    }

    // 別人的案件跟不存在一樣：404，不透露案件存在
    [Fact]
    public async Task GetMyCase_OtherCitizensCase_ReturnsNotFound()
    {
        var others = await _citizenB.SubmitCaseAsync();

        var response = await _citizenA.GetAsync($"api/my/cases/{others.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    // ── 補件 ─────────────────────────────────────────────

    // 10/15 的漏洞：以前任何人知道案件 Id 就能補件。B 的案件確實在等補件，A 補不了（404），B 自己可以
    [Fact]
    public async Task SubmitSupplement_OtherCitizensCase_ReturnsNotFound_OwnerSucceeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var others = await _citizenB.SubmitCaseAsync();
        await _dispatcherClient.AssignAsync(others.Id, TestTokens.OfficerDepartmentId);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _officerClient.PostAsync($"api/cases/{others.Id}/review", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _officerClient.PostAsJsonAsync($"api/cases/{others.Id}/supplement-requests",
                new { reason = "缺照片" }, cancellationToken)).StatusCode);

        var byOther = await _citizenA.PostAsJsonAsync($"api/my/cases/{others.Id}/supplement-submission",
            new { content = "冒名補件" }, cancellationToken);
        var byOwner = await _citizenB.PostAsJsonAsync($"api/my/cases/{others.Id}/supplement-submission",
            new { content = "已補上照片說明" }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, byOther.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
    }
}
