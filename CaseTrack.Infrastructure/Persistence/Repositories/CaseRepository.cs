using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Cases;
using CaseTrack.Application.Common;
using CaseTrack.Domain.Cases;
using Microsoft.EntityFrameworkCore;

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

    // 讀取專用：不經過聚合，直接在 SQL 裡只選列表需要的欄位（陳情人的欄位根本不會被讀出來）
    public async Task<PagedResult<CaseSummary>> ListAsync(
        CaseListQuery query,
        CaseAccessScope scope,
        CancellationToken cancellationToken)
    {
        // 什麼都看不到：不用查資料庫
        if (scope.Kind == CaseAccessScopeKind.None)
        {
            return new PagedResult<CaseSummary>([], query.Page, query.PageSize, 0);
        }

        // AsNoTracking：只是讀取，不放進變更追蹤器。到 CountAsync／ToListAsync 之前都只是在組合 SQL
        var cases = _dbContext.Cases
            .AsNoTracking()
            .ApplyScope(scope);

        if (query.Status is { } status)
        {
            cases = cases.Where(c => c.Status == status);
        }

        // 主旨「或」案件編號包含關鍵字：要寫在同一個 Where 裡，連續兩個 Where 是 AND
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            cases = cases.Where(c => c.Subject.Contains(keyword) || c.CaseNumber.Contains(keyword));
        }

        // 總筆數要從套用了範圍和篩選的查詢算：從整張表算的話，數字是錯的，還會洩漏全系統的案件數
        var totalCount = await cases.CountAsync(cancellationToken);

        // 排序要穩定：只有 SubmittedAt 的話，同一個時間的案件每次順序可能不同，翻頁時會重複或漏掉。CaseNumber 是唯一的。
        // Select 直接寫 new：EF 看得到用了哪些屬性，只選那些欄位；呼叫自訂方法的話，EF 會把整個 Case（含陳情人）讀出來
        var items = await cases
            .OrderByDescending(c => c.SubmittedAt)
            .ThenByDescending(c => c.CaseNumber)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new CaseSummary(c.Id, c.CaseNumber, c.Subject, c.Status, c.SubmittedAt, c.DepartmentId))
            .ToListAsync(cancellationToken);

        return new PagedResult<CaseSummary>(items, query.Page, query.PageSize, totalCount);
    }
}
