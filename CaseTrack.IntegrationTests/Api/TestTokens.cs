using System.Net.Http.Headers;
using System.Text;
using CaseTrack.Application.Authorization;
using CaseTrack.Infrastructure.Persistence.Configurations;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CaseTrack.IntegrationTests.Api;

// 測試直接簽發 token，不經過 /dev/token：測試要測的是「API 怎麼驗證 token」，簽發 token 的是 IdP，不是 API 的一部分
internal static class TestTokens
{
    // 測試專用的金鑰，寫死在這裡沒有問題：它只用來簽給記憶體裡的測試伺服器驗證，不是機密。
    // 寫死的好處是測試不依賴任何人電腦上的 User Secrets，換電腦或在 CI 上跑結果都一樣。
    public static readonly string SigningKey =
        Convert.ToBase64String(Encoding.UTF8.GetBytes("casetrack-integration-tests-key!"));

    // 跟 CaseTrack.Api 的 appsettings.json 一致
    public const string Issuer = "casetrack-dev";
    public const string Audience = "casetrack-api";

    // 工務局的承辦人：token 裡的單位跟案件的 DepartmentId 是同一種值（Guid），授權時才能比對
    public const string OfficerUserId = "u-officer-1";
    public static readonly Guid OfficerDepartmentId = DepartmentSeed.PublicWorks;

    // 分派人員不屬於任何承辦單位
    public const string DispatcherUserId = "u-dispatcher-1";

    // 民眾：沒有單位
    public const string CitizenUserId = "u-citizen-1";

    public static string CreateCitizenToken(string userId = CitizenUserId) =>
        Create(userId, [Roles.Citizen]);

    public static string CreateOfficerToken() =>
        Create(OfficerUserId, [Roles.CaseOfficer], OfficerDepartmentId.ToString());

    public static string CreateDispatcherToken() =>
        Create(DispatcherUserId, [Roles.Dispatcher]);

    public static string Create(
        string userId,
        string[] roles,
        string? departmentId = null,
        DateTimeOffset? expiresAt = null,
        string? signingKey = null,
        string audience = Audience)
    {
        var expires = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(30);
        var claims = new Dictionary<string, object>
        {
            ["sub"] = userId,
            ["name"] = userId,
            ["role"] = roles,
        };
        if (departmentId is not null)
        {
            claims["department_id"] = departmentId;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            // 過期的 token 也要能產生：NotBefore 和 IssuedAt 一律設在到期之前
            IssuedAt = expires.AddHours(-1).UtcDateTime,
            NotBefore = expires.AddHours(-1).UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(signingKey ?? SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
    }

    public static HttpClient WithBearerToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
