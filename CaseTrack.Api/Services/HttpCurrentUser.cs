using CaseTrack.Application.Abstractions;

namespace CaseTrack.Api.Services;

public class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }
    public string Name => _httpContextAccessor.HttpContext is null ? "system" : "anonymous";
}
