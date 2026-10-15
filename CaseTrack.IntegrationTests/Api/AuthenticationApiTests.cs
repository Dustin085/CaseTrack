using System.Net;
using System.Net.Http.Json;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Cases;
using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CaseTrack.IntegrationTests.Api;

// 身分驗證的資安測試：每一種「不該通過」的 token 都要確實被擋下來
[Collection(nameof(DatabaseCollection))]
public class AuthenticationApiTests : IClassFixture<CaseTrackApiFactory>
{
    private readonly CaseTrackApiFactory _factory;

    public AuthenticationApiTests(CaseTrackApiFactory factory)
    {
        _factory = factory;
    }

    // 正面對照：合法的 token 能通過驗證（不存在的案件 → 404）。
    // 沒有這個測試的話，下面的 401 測試可能只是因為「什麼 token 都過不了」而通過
    [Fact]
    public async Task GetCase_WithValidToken_PassesAuthentication()
    {
        var response = await GetNonexistentCaseAsync(TestTokens.CreateOfficerToken());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCase_WithoutToken_ReturnsUnauthorized()
    {
        var response = await GetNonexistentCaseAsync(token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ClockSkew 設為 1 分鐘：過期超過 1 分鐘就必須被拒絕
    [Fact]
    public async Task GetCase_WithExpiredToken_ReturnsUnauthorized()
    {
        var token = TestTokens.Create(TestTokens.OfficerUserId, [Roles.CaseOfficer],
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-2));

        var response = await GetNonexistentCaseAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 不知道金鑰的人自己簽的 token（例如自己把角色改成管理者）
    [Fact]
    public async Task GetCase_WithTokenSignedByOtherKey_ReturnsUnauthorized()
    {
        var otherKey = Convert.ToBase64String("someone-else-signing-key-32bytes"u8.ToArray());
        var token = TestTokens.Create(TestTokens.OfficerUserId, [Roles.CaseOfficer], signingKey: otherKey);

        var response = await GetNonexistentCaseAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 同一個 IdP 發給別的系統的 token，不能拿來呼叫這個 API
    [Fact]
    public async Task GetCase_WithTokenForOtherAudience_ReturnsUnauthorized()
    {
        var token = TestTokens.Create(TestTokens.OfficerUserId, [Roles.CaseOfficer], audience: "other-system");

        var response = await GetNonexistentCaseAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 正面對照：開發環境有 /dev/token。沒有這個測試的話，下面的 404 可能只是因為路徑打錯
    [Fact]
    public async Task DevToken_InDevelopment_ReturnsToken()
    {
        var response = await RequestDevTokenAsync(_factory.CreateClient());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // 最重要的資安測試：/dev/token 不驗證密碼，正式環境一定不能存在
    [Fact]
    public async Task DevToken_InProduction_ReturnsNotFound()
    {
        await using var productionFactory = _factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));

        var response = await RequestDevTokenAsync(productionFactory.CreateClient());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // 稽核記錄的是 token 裡的使用者 Id（sub），不是姓名，也不是 anonymous
    [Fact]
    public async Task ReviewCase_WithOfficerToken_RecordsUserIdInAudit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitResponse = await _factory.CreateCitizenClient().PostAsJsonAsync("api/cases",
            new { subject = "路燈不亮", content = "中山路路燈故障", petitioner = new { name = "王小明", phone = "0912345678" } },
            cancellationToken);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<SubmitCaseResult>(cancellationToken);
        Assert.NotNull(submitted);

        var reviewResponse = await _factory.CreateOfficerClient()
            .PostAsync($"api/cases/{submitted.Id}/review", null, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, reviewResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var changedBy = await scope.ServiceProvider.GetRequiredService<CaseTrackDbContext>().AuditEntries
            .Where(a => a.EntityType == nameof(Case) && a.EntityId == submitted.Id.ToString())
            .OrderBy(a => a.Id)
            .Select(a => a.ChangedBy)
            .ToListAsync(cancellationToken);
        Assert.Equal(["anonymous", TestTokens.OfficerUserId], changedBy);
    }

    private async Task<HttpResponseMessage> GetNonexistentCaseAsync(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null)
        {
            client.WithBearerToken(token);
        }
        return await client.GetAsync($"api/cases/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> RequestDevTokenAsync(HttpClient client) =>
        client.PostAsJsonAsync("dev/token",
            new { userId = "u1", name = "測試", roles = new[] { Roles.CaseOfficer } },
            TestContext.Current.CancellationToken);
}
