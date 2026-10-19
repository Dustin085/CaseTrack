using CaseTrack.Api.Authentication;
using CaseTrack.Api.BackgroundJobs;
using CaseTrack.Api.ExceptionHandling;
using CaseTrack.Api.Services;
using CaseTrack.Application;
using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Infrastructure;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// enum 在 JSON 裡用名稱（"UnderReview"）而不是數字：數字前端看不懂，而且 enum 插入新成員後數字會對不上
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
// Swagger UI 的 Authorize 按鈕：貼上 /dev/token 拿到的 token，之後需要登入的端點會自動帶上 Authorization 標頭
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(BearerSecurityOperationFilter.SchemeId, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "開發環境先呼叫 POST /dev/token 取得 token，只貼上 token 本身（不用加 Bearer）",
    });
    options.OperationFilter<BearerSecurityOperationFilter>();
});

builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("CaseTrack")
    ?? throw new InvalidOperationException("缺少連線字串 'CaseTrack'"));
builder.Services.AddApplication();

// 設定檔綁定是用反射建立物件，會繞過 required 的檢查，所以缺少的值要自己檢查，並給出看得懂的錯誤訊息
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("缺少 Jwt 設定");
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    throw new InvalidOperationException(
        "缺少 Jwt:SigningKey。開發環境請用 dotnet user-secrets 設定，正式環境請用環境變數 Jwt__SigningKey");
}
var signingKeyBytes = Convert.FromBase64String(jwtOptions.SigningKey);
if (signingKeyBytes.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey 至少要 32 個位元組（HS256 的要求）");
}
var signingKey = new SymmetricSecurityKey(signingKeyBytes);

// 這個 API 只驗證 token、不負責登入（Resource Server）：token 由 IdP 簽發，開發環境由 /dev/token 簽發
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // 預設會把 "role"、"sub" 改名成微軟舊格式的長名稱，IsInRole 會永遠回傳 false
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = signingKey,
            NameClaimType = CaseTrackClaimTypes.Name,
            RoleClaimType = CaseTrackClaimTypes.Role,
            // 預設 5 分鐘：過期後還會被接受 5 分鐘，對 30 分鐘效期的 token 來說太長
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

// 授權的第 1 層：角色。管理者不在任何一個 policy 裡（最小權限），呼叫案件的端點都是 403
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.CaseReader, policy => policy.RequireRole(Roles.CaseOfficer, Roles.Supervisor, Roles.Dispatcher))
    .AddPolicy(Policies.CaseHandler, policy => policy.RequireRole(Roles.CaseOfficer, Roles.Supervisor))
    .AddPolicy(Policies.CaseDispatcher, policy => policy.RequireRole(Roles.Dispatcher))
    .AddPolicy(Policies.Citizen, policy => policy.RequireRole(Roles.Citizen));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<NotFoundExceptionHandler>();
builder.Services.AddExceptionHandler<RequestValidationExceptionHandler>();
builder.Services.AddExceptionHandler<ConcurrencyConflictExceptionHandler>();

builder.Services.AddOptions<OverdueScanOptions>().BindConfiguration("OverdueScan");

builder.Services.AddHostedService<OverdueSupplementScanner>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

var app = builder.Build();
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 順序不能反：先從 token 讀出使用者（HttpContext.User），才能檢查權限
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 不驗證密碼、指定誰就發誰：只能存在於開發環境。正式環境的 token 由 IdP 簽發
if (app.Environment.IsDevelopment())
{
    app.MapDevTokenEndpoint(jwtOptions, signingKey);
}

app.Run();
