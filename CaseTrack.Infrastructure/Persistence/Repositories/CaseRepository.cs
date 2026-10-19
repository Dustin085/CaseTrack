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

        return _dbContext.Cases
            .Include(c => c.SupplementRequests)
            .ApplyScope(scope)
            .Where(c => c.Id == id)
            .SingleOrDefaultAsync(cancellationToken);
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
