using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace CaseTrack.Infrastructure.Persistence.Repositories;

internal sealed class CaseRepository : ICaseRepository
{
    private readonly CaseTrackDbContext _dbContext;

    public CaseRepository(CaseTrackDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(Case @case)
    {
        _dbContext.Cases.Add(@case);
    }

    // 範圍直接變成 SQL 的 WHERE：不在範圍內的案件查不到（null），對外跟「不存在」一樣都是 404
    public Task<Case?> GetByIdAsync(Guid id, CaseAccessScope scope, CancellationToken cancellationToken = default)
    {
        // 什麼都看不到：不用查資料庫
        if (scope.Kind == CaseAccessScopeKind.None)
        {
            return Task.FromResult<Case?>(null);
        }
        var query = _dbContext.Cases
            .Include(c => c.SupplementRequests)
            .Where(c => c.Id == id);

        // 每一種範圍都明確寫出來，不認識的種類拋例外。
        // 不能寫成「Department 才過濾、其他不過濾」：那樣 None 和之後新增的種類都會變成「全部」
        query = scope.Kind switch
        {
            CaseAccessScopeKind.All => query,
            CaseAccessScopeKind.Department => query.Where(c => c.DepartmentId == scope.DepartmentId),
            CaseAccessScopeKind.None => throw new UnreachableException("None 已經在上面直接回傳"),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope.Kind, "不支援的範圍")
        };

        return query.SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetOverdueSupplementCaseIdsAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
    {
        var deadline = now - Case.SupplementDeadline;
        return await _dbContext.Cases
            .AsNoTracking()
            .Where(c => c.Status == CaseStatus.AwaitingSupplement
            && c.SupplementRequests.Any(r => r.SubmittedAt == null && r.RequestedAt <= deadline))
            .OrderBy(c => c.CaseNumber)
            .Select(c => c.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
