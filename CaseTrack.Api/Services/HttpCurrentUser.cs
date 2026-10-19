using System.Collections.Frozen;
using System.Security.Claims;
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
            if (_httpContextAccessor.HttpContext is null)
            {
                return System;
            }

            // 有請求但沒登入：只有不需要登入的端點（例如開發環境的 /dev/token）。
            // 會寫入資料的端點都要登入（民眾也是），稽核紀錄出現 anonymous 就代表有端點漏了 [Authorize]
            if (AuthenticatedUser is null)
            {
                return Anonymous;
            }

            // 有登入卻沒有 sub：IdP 的設定有問題。寧可失敗，也不要把登入者的操作記成 anonymous，
            // 稽核紀錄一旦寫錯就追查不到責任
            return AuthenticatedUser.FindFirst(CaseTrackClaimTypes.Subject)?.Value
                ?? throw new InvalidOperationException($"token 缺少 {CaseTrackClaimTypes.Subject} claim，無法識別使用者");
        }
    }

    // FrozenSet：真的不能修改（HashSet 轉型回去就能 Add），而且適合「建立一次、查詢很多次」
    public IReadOnlySet<string> Roles =>
        AuthenticatedUser?.FindAll(CaseTrackClaimTypes.Role).Select(c => c.Value).ToFrozenSet()
        ?? FrozenSet<string>.Empty;

    public Guid? DepartmentId
    {
        get
        {
            // 沒有單位是正常的：分派人員、管理者、民眾、排程都沒有
            var value = AuthenticatedUser?.FindFirst(CaseTrackClaimTypes.DepartmentId)?.Value;
            if (value is null)
            {
                return null;
            }

            // 有單位卻不是 Guid：IdP 的設定有問題（例如送了舊格式的代碼 "PWD"）。
            // 當成 null 的話，承辦人會變成「沒有單位」而什麼案件都看不到、一直 404，卻沒有任何錯誤訊息
            if (!Guid.TryParse(value, out var departmentId))
            {
                throw new InvalidOperationException(
                    $"token 的 {CaseTrackClaimTypes.DepartmentId} 不是合法的 Guid：{value}");
            }
            return departmentId;
        }
    }

    // 有登入的使用者；排程（沒有 HttpContext）或沒有登入時是 null。
    // 注意：沒登入的請求也有 HttpContext.User（一個沒有任何 claim 的匿名使用者），不是 null，所以要檢查 IsAuthenticated。
    // 屬性模式一次檢查三件事：User 不是 null、Identity 不是 null、IsAuthenticated 是 true（bool? 的 null 也不算通過）
    private ClaimsPrincipal? AuthenticatedUser =>
        _httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;
}
