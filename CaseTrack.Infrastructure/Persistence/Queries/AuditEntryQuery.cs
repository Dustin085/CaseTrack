using CaseTrack.Application.Auditing;
using CaseTrack.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.Infrastructure.Persistence.Queries;

internal sealed class AuditEntryQuery : IAuditLogQuery
{
    private readonly CaseTrackDbContext _dbContext;

    public AuditEntryQuery(CaseTrackDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // 條件都是完全相符（不是 LIKE）：EntityType、EntityId、ChangedBy 是識別碼，部分比對沒有意義，也用不到索引
    public async Task<PagedResult<AuditRecord>> ListAsync(
        string? entityType, string? entityId, string? changedBy,
        DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var entries = _dbContext.AuditEntries.AsNoTracking();
        if (entityType is not null)
        {
            entries = entries.Where(e => e.EntityType == entityType);
        }
        if (entityId is not null)
        {
            entries = entries.Where(e => e.EntityId == entityId);
        }
        if (changedBy is not null)
        {
            entries = entries.Where(e => e.ChangedBy == changedBy);
        }
        if (from is not null)
        {
            entries = entries.Where(e => e.ChangedAt >= from);
        }
        if (to is not null)
        {
            entries = entries.Where(e => e.ChangedAt < to);
        }

        var totalCount = await entries.CountAsync(cancellationToken);
        // Id 是遞增的，同一個時間點（同一次 SaveChanges）的多筆也有固定順序
        var items = await entries
            .OrderByDescending(e => e.ChangedAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditRecord(e.Id, e.EntityType, e.EntityId, e.Action, e.Changes, e.ChangedBy, e.ChangedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditRecord>(items, page, pageSize, totalCount);
    }
}
