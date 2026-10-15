namespace CaseTrack.Api.Authentication;

// JWT 的設定。SigningKey 是機密：開發環境放 User Secrets，正式環境用環境變數，不能寫進 appsettings.json。
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string SigningKey { get; init; }   // Base64
}
