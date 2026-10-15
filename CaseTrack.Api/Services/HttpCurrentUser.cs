using CaseTrack.Api.Authentication;
using CaseTrack.Application.Abstractions;

namespace CaseTrack.Api.Services;

public sealed class HttpCurrentUser : ICurrentUser
{
    public const string System = "system";
    public const string Anonymous = "anonymous";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string UserId
    {
        get
        {
            // 沒有 HTTP 請求：背景排程
            var user = _httpContextAccessor.HttpContext?.User;
            if (user is null)
            {
                return System;
            }

            // 有請求但沒登入：民眾送出陳情、補件
            if (user.Identity?.IsAuthenticated != true)
            {
                return Anonymous;
            }

            // 有登入卻沒有 sub：IdP 的設定有問題。寧可失敗，也不要把登入者的操作記成 anonymous，
            // 稽核紀錄一旦寫錯就追查不到責任
            return user.FindFirst(CaseTrackClaimTypes.Subject)?.Value
                ?? throw new InvalidOperationException($"token 缺少 {CaseTrackClaimTypes.Subject} claim，無法識別使用者");
        }
    }
}
