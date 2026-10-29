using CaseTrack.Application.Authorization;
using CaseTrack.Application.Reports;
using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.Infrastructure.Persistence.Queries;

// 報表的讀取查詢。範圍一樣先套用 ApplyScope：報表也是一種查詢，承辦人不能透過報表看到別單位的案件
internal sealed class CaseReportQuery : ICaseReportQuery
{
    private readonly CaseTrackDbContext _dbContext;

    public CaseReportQuery(CaseTrackDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<DepartmentStatusCount>> CountByDepartmentAndStatusAsync(
        DateTimeOffset from, DateTimeOffset to, CaseAccessScope scope, CancellationToken cancellationToken)
    {
        // GROUP BY 在資料庫做，只傳回「單位 × 狀態」的件數，不把案件一筆一筆讀出來
        var counts = await InPeriod(from, to, scope)
            .GroupBy(c => new { c.DepartmentId, c.Status })
            .Select(g => new { g.Key.DepartmentId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // 單位名稱另外查（只有幾筆），在記憶體對照：未分派（null）的名稱也是 null
        var names = await _dbContext.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        return counts
            .Select(c => new DepartmentStatusCount(
                c.DepartmentId,
                c.DepartmentId is { } id ? names.GetValueOrDefault(id) : null,
                c.Status,
                c.Count))
            .ToList();
    }

    public async Task<IReadOnlyList<CaseListRow>> ListAsync(
        DateTimeOffset from, DateTimeOffset to, CaseStatus? status, CaseAccessScope scope, int limit,
        CancellationToken cancellationToken)
    {
        var cases = InPeriod(from, to, scope);
        if (status is { } s)
        {
            cases = cases.Where(c => c.Status == s);
        }

        // LEFT JOIN 單位：未分派的案件也要出現在清單裡。
        // Case 沒有 Department 的導覽屬性（不同的聚合），所以用 GroupJoin + DefaultIfEmpty 寫 LEFT JOIN
        return await cases
            .OrderBy(c => c.SubmittedAt)
            .ThenBy(c => c.CaseNumber)
            .Take(limit)
            .GroupJoin(_dbContext.Departments, c => c.DepartmentId, d => (Guid?)d.Id, (c, ds) => new { c, ds })
            .SelectMany(x => x.ds.DefaultIfEmpty(), (x, d) => new CaseListRow(
                x.c.CaseNumber, x.c.Subject, x.c.Status, d == null ? null : d.Name, x.c.SubmittedAt))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<Case> InPeriod(DateTimeOffset from, DateTimeOffset to, CaseAccessScope scope) =>
        _dbContext.Cases
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(c => c.SubmittedAt >= from && c.SubmittedAt < to);
}
