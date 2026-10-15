using System.Security.Claims;
using CaseTrack.Api.Services;
using Microsoft.AspNetCore.Http;

namespace CaseTrack.Tests;

public class HttpCurrentUserTests
{
    [Fact]
    public void UserId_WithoutHttpContext_ReturnsSystem()
    {
        var currentUser = CreateCurrentUser(httpContext: null);

        Assert.Equal(HttpCurrentUser.System, currentUser.UserId);
    }

    [Fact]
    public void UserId_NotAuthenticated_ReturnsAnonymous()
    {
        var currentUser = CreateCurrentUser(new DefaultHttpContext());

        Assert.Equal(HttpCurrentUser.Anonymous, currentUser.UserId);
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

    private static HttpCurrentUser CreateCurrentUser(HttpContext? httpContext) =>
        new(new HttpContextAccessor { HttpContext = httpContext });

    // ClaimsIdentity 有指定驗證方式（authenticationType）才算「已登入」
    private static DefaultHttpContext CreateAuthenticatedContext(params Claim[] claims) =>
        new() { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")) };
}
