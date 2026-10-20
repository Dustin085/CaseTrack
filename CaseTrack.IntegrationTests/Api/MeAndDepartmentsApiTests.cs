using System.Net;
using System.Net.Http.Json;
using CaseTrack.Api.Contracts;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Departments;
using CaseTrack.Infrastructure.Persistence.Configurations;
using static CaseTrack.IntegrationTests.Api.ApiTestHelpers;

namespace CaseTrack.IntegrationTests.Api;

// 前端需要的兩個查詢：目前登入的是誰（/api/me）、單位清單（/api/departments）
[Collection(nameof(DatabaseCollection))]
public class MeAndDepartmentsApiTests : IClassFixture<CaseTrackApiFactory>
{
    private readonly CaseTrackApiFactory _factory;

    public MeAndDepartmentsApiTests(CaseTrackApiFactory factory)
    {
        _factory = factory;
    }

    // ── /api/me ──────────────────────────────────────────

    // 姓名要是 claim 的值本身：Claim.ToString() 會變成 "name: u-officer-1"
    [Fact]
    public async Task GetMe_ByOfficer_ReturnsIdentityUsedByBackend()
    {
        var me = await _factory.CreateOfficerClient().GetFromJsonAsync<MeResponse>(
            "api/me", JsonOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(me);
        Assert.Equal(TestTokens.OfficerUserId, me.UserId);
        Assert.Equal(TestTokens.OfficerUserId, me.Name);
        Assert.Equal(Roles.CaseOfficer, Assert.Single(me.Roles));
        Assert.Equal(TestTokens.OfficerDepartmentId, me.DepartmentId);
    }

    [Fact]
    public async Task GetMe_ByCitizen_HasNoDepartment()
    {
        var me = await _factory.CreateCitizenClient().GetFromJsonAsync<MeResponse>(
            "api/me", JsonOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(me);
        Assert.Equal(TestTokens.CitizenUserId, me.UserId);
        Assert.Equal(Roles.Citizen, Assert.Single(me.Roles));
        Assert.Null(me.DepartmentId);
    }

    // ── /api/departments ─────────────────────────────────

    // 種子資料的 3 個單位，依代碼排序（ENV、PWD、TRA）：順序固定，下拉選單才不會每次不一樣。
    // 這個順序跟依 Id 排序（ENV、TRA、PWD）、依名稱排序（交通局、工務局、環境保護局）都不同，排錯欄位會被抓到
    [Fact]
    public async Task ListDepartments_ReturnsSeededDepartmentsOrderedByCode()
    {
        var departments = await _factory.CreateDispatcherClient().GetFromJsonAsync<List<DepartmentSummary>>(
            "api/departments", JsonOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(departments);
        Assert.Equal(
            [DepartmentSeed.EnvironmentalProtection, DepartmentSeed.PublicWorks, DepartmentSeed.Transportation],
            departments.Select(d => d.Id));
        Assert.All(departments, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
    }

    // 公開資訊：民眾也看得到
    [Fact]
    public async Task ListDepartments_ByCitizen_ReturnsOk()
    {
        var response = await _factory.CreateCitizenClient().GetAsync("api/departments", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 沒有登入 ─────────────────────────────────────────

    [Theory]
    [InlineData("api/me")]
    [InlineData("api/departments")]
    public async Task Get_WithoutToken_ReturnsUnauthorized(string url)
    {
        var response = await _factory.CreateClient().GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
