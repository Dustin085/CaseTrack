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
    }
}
