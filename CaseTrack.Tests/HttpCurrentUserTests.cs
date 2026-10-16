using System.Security.Claims;
using CaseTrack.Api.Services;
using CaseTrack.Application.Authorization;
using CaseTrack.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Http;

namespace CaseTrack.Tests;

public class HttpCurrentUserTests
{
    [Fact]
    public void UserId_WithoutHttpContext_ReturnsSystem()
    {
        var currentUser = CreateCurrentUser(httpContext: null);

        Assert.Equal(HttpCurrentUser.System, currentUser.UserId);
        Assert.Empty(currentUser.Roles);
        Assert.Null(currentUser.DepartmentId);
    }

    [Fact]
    public void UserId_NotAuthenticated_ReturnsAnonymous()
    {
        var currentUser = CreateCurrentUser(new DefaultHttpContext());

        Assert.Equal(HttpCurrentUser.Anonymous, currentUser.UserId);
        Assert.Empty(currentUser.Roles);
        Assert.Null(currentUser.DepartmentId);
    }

    [Fact]
    public void UserId_Authenticated_ReturnsSubjectNotName()
    {
        var currentUser = CreateCurrentUser(CreateAuthenticatedContext(
            new Claim("sub", "u-officer-1"), new Claim("name", "王承辦")));

        Assert.Equal("u-officer-1", currentUser.UserId);
    }

    // 登入了卻識別不出是誰：不能默默記成 anonymous
    [Fact]
    public void UserId_AuthenticatedWithoutSubject_Throws()
    {
        var currentUser = CreateCurrentUser(CreateAuthenticatedContext(new Claim("name", "王承辦")));

        Assert.Throws<InvalidOperationException>(() => currentUser.UserId);
    }

    // 一張 token 可以有好幾個 role claim（例如承辦人兼主管），全部都要讀到，而且不能多出別的角色
    [Fact]
    public void Roles_AuthenticatedWithTwoRoles_ReturnsBoth()
    {
        var currentUser = CreateCurrentUser(CreateAuthenticatedContext(
            new Claim("sub", "u-officer-1"),
            new Claim("role", Roles.CaseOfficer),
            new Claim("role", Roles.Supervisor)));

        Assert.Equal(2, currentUser.Roles.Count);
        Assert.Contains(Roles.CaseOfficer, currentUser.Roles);
        Assert.Contains(Roles.Supervisor, currentUser.Roles);
    }

    // 不用 new Guid()：那是 Guid.Empty（全部是 0），實作有 bug 回傳預設值時，測試照樣會通過
    [Fact]
    public void DepartmentId_AuthenticatedWithDepartment_ReturnsParsedGuid()
    {
        var currentUser = CreateCurrentUser(CreateAuthenticatedContext(
            new Claim("sub", "u-officer-1"),
            new Claim("role", Roles.CaseOfficer),
            new Claim("department_id", DepartmentSeed.PublicWorks.ToString())));

        Assert.Equal(DepartmentSeed.PublicWorks, currentUser.DepartmentId);
    }

    // 分派人員的 token 本來就沒有單位：是 null，不是例外
    [Fact]
    public void DepartmentId_DispatcherWithoutDepartment_ReturnsNull()
    {
        var currentUser = CreateCurrentUser(CreateAuthenticatedContext(
            new Claim("sub", "u-dispatcher-1"),
            new Claim("role", Roles.Dispatcher)));

        Assert.Null(currentUser.DepartmentId);
    }

    // 有單位卻不是 Guid（例如 IdP 送了舊格式的代碼）：設定錯誤要大聲失敗，不能當成「沒有單位」
    [Fact]
    public void DepartmentId_AuthenticatedWithNonGuidDepartment_Throws()
    {
        var currentUser = CreateCurrentUser(CreateAuthenticatedContext(
            new Claim("sub", "u-officer-1"),
            new Claim("role", Roles.CaseOfficer),
            new Claim("department_id", "TRA")));

        var exception = Assert.Throws<InvalidOperationException>(() => currentUser.DepartmentId);
        Assert.Contains("TRA", exception.Message);
    }

    private static HttpCurrentUser CreateCurrentUser(HttpContext? httpContext) =>
        new(new HttpContextAccessor { HttpContext = httpContext });

    // ClaimsIdentity 有指定驗證方式（authenticationType）才算「已登入」
    private static DefaultHttpContext CreateAuthenticatedContext(params Claim[] claims) =>
        new() { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")) };
}
