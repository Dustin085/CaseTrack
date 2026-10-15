using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CaseTrack.IntegrationTests.Api;

public sealed class CaseTrackApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:CaseTrack", DatabaseFixture.ConnectionString);
        // 防止 scanner 影響測試結果
        builder.UseSetting("OverdueScan:Enabled", "false");
        // 測試專用的金鑰，取代開發者電腦上的 User Secrets
        builder.UseSetting("Jwt:SigningKey", TestTokens.SigningKey);
    }

    // 沒有登入的民眾
    public HttpClient CreateCitizenClient() => CreateClient();

    // 本單位的承辦人
    public HttpClient CreateOfficerClient() => CreateClient().WithBearerToken(TestTokens.CreateOfficerToken());
}
