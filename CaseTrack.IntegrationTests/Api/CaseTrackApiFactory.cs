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
        // 附件不能存進原始碼資料夾（appsettings.json 的相對路徑會相對於 Api 專案的資料夾）
        builder.UseSetting("FileStorage:RootPath", TestData.FileStorageRoot);
    }

    // 登入的民眾。要測「民眾 A 看不到民眾 B 的案件」時，傳入不同的 userId
    public HttpClient CreateCitizenClient(string userId = TestTokens.CitizenUserId) =>
        CreateClient().WithBearerToken(TestTokens.CreateCitizenToken(userId));

    // 工務局的承辦人
    public HttpClient CreateOfficerClient() => CreateClient().WithBearerToken(TestTokens.CreateOfficerToken());

    // 分派人員
    public HttpClient CreateDispatcherClient() => CreateClient().WithBearerToken(TestTokens.CreateDispatcherToken());

    // 指定角色和單位的使用者，例如別的單位的承辦人、管理者
    public HttpClient CreateClientFor(string userId, string[] roles, Guid? departmentId = null) =>
        CreateClient().WithBearerToken(TestTokens.Create(userId, roles, departmentId?.ToString()));
}
