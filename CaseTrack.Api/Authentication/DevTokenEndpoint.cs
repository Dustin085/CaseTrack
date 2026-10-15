using CaseTrack.Application.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CaseTrack.Api.Authentication;

public sealed record DevTokenRequest(string UserId, string Name, string[] Roles, string? DepartmentId);

public sealed record DevTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

// 開發環境專用：不驗證密碼，指定誰就發誰的 token，用來代替正式環境的 IdP（AD／單一登入）。
// 有了它就能把自己變成任何角色，所以只能在 Development 註冊（見 Program.cs），而且用 Minimal API：
// Controller 會被自動掃描註冊，不管什麼環境都存在；Minimal API 寫在 if 裡面，正式環境根本不會被註冊。
public static class DevTokenEndpoint
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public static IEndpointConventionBuilder MapDevTokenEndpoint(
        this IEndpointRouteBuilder endpoints, JwtOptions jwtOptions, SecurityKey signingKey)
    {
        return endpoints.MapPost("/dev/token", (DevTokenRequest request, TimeProvider timeProvider) =>
            {
                // 角色打錯字的話，token 照樣發得出來，但之後每個請求都會莫名其妙地 403，所以在這裡就擋掉
                var unknownRoles = request.Roles.Where(r => !Roles.All.Contains(r)).ToArray();
                if (string.IsNullOrWhiteSpace(request.UserId) || unknownRoles.Length > 0)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(request.UserId)] = string.IsNullOrWhiteSpace(request.UserId) ? ["必填"] : [],
                        [nameof(request.Roles)] = unknownRoles.Select(r => $"不認識的角色：{r}").ToArray(),
                    }.Where(e => e.Value.Length > 0).ToDictionary());
                }

                var now = timeProvider.GetUtcNow();
                var expiresAt = now + Lifetime;
                var claims = new Dictionary<string, object>
                {
                    [CaseTrackClaimTypes.Subject] = request.UserId,
                    [CaseTrackClaimTypes.Name] = request.Name,
                    [CaseTrackClaimTypes.Role] = request.Roles,
                };
                if (!string.IsNullOrWhiteSpace(request.DepartmentId))
                {
                    claims[CaseTrackClaimTypes.DepartmentId] = request.DepartmentId;
                }

                var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                {
                    Issuer = jwtOptions.Issuer,
                    Audience = jwtOptions.Audience,
                    IssuedAt = now.UtcDateTime,
                    NotBefore = now.UtcDateTime,
                    Expires = expiresAt.UtcDateTime,
                    Claims = claims,
                    SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
                });

                return Results.Ok(new DevTokenResponse(token, expiresAt));
            })
            .AllowAnonymous()
            .WithTags("Development");
    }
}
