using CaseTrack.Application.Authorization;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Infrastructure.Persistence.Repositories;

internal static class CaseAccessScopeQueryExtensions
{
    public static IQueryable<Case> ApplyScope(
        this IQueryable<Case> query,
        CaseAccessScope scope)
    {
        // 每一種範圍都明確寫出來，不認識的種類拋例外。
        // 不能寫成「Department 才過濾、其他不過濾」：那樣 None 和之後新增的種類都會變成「全部」
        return scope.Kind switch
        {
            CaseAccessScopeKind.All => query,
            CaseAccessScopeKind.Department => query.Where(c => c.DepartmentId == scope.DepartmentId),
            CaseAccessScopeKind.None => query.Where(c => false),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope.Kind, "不支援的範圍")
        };
    }
}
