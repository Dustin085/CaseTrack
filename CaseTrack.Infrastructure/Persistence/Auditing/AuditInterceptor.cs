using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CaseTrack.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CaseTrack.Infrastructure.Persistence.Auditing;

// 在 SaveChanges 之前，把變更追蹤器裡每一筆新增／修改／刪除的資料轉成 AuditEntry，
// 加進同一個 DbContext，跟業務資料在同一個交易裡寫入。
// 限制：
// - 只看得到經過變更追蹤器的修改；ExecuteUpdate／ExecuteSql／Dapper 直接執行的 SQL 不會被記錄。
// - 主鍵由資料庫產生的 entity，存檔前的主鍵是暫時值；目前被稽核的 entity 主鍵都由 Domain 產生，沒有這個問題。
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    // 共用同一個實例：JsonSerializerOptions 會快取序列化的中繼資料，每次 new 會很慢
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // enum 存成名稱，不存數字（數字看不懂，而且 enum 插入新值後舊紀錄會對不上）
        Converters = { new JsonStringEnumConverter() },
        // 中文不要跳脫成 \uXXXX；稽核紀錄存在資料庫，不會直接輸出到 HTML
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public AuditInterceptor(ICurrentUser currentUser, TimeProvider timeProvider)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } dbContext)
        {
            AddAuditEntries(dbContext);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditEntries(DbContext dbContext)
    {
        var changedBy = _currentUser.Name;
        var changedAt = _timeProvider.GetUtcNow();

        // 先用 ToList() 固定下來：等一下要把 AuditEntry 加進同一個 DbContext，
        // 一邊列舉變更追蹤器、一邊新增，會修改正在列舉的集合
        var auditEntries = dbContext.ChangeTracker.Entries()
            .Where(entry => entry.Entity is not AuditEntry)
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => new AuditEntry(
                entry.Metadata.ClrType.Name,
                GetPrimaryKey(entry),
                entry.State.ToString(),
                JsonSerializer.Serialize(GetChanges(entry), JsonOptions),
                changedBy,
                changedAt))
            .ToList();

        dbContext.AddRange(auditEntries);
    }

    // 複合主鍵用逗號串起來
    private static string GetPrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey()!;
        return string.Join(",", key.Properties.Select(p => entry.Property(p.Name).CurrentValue));
    }

    // 新增：所有欄位的新值；修改：只有被改過的欄位的前後值；刪除：所有欄位的舊值。
    // 排除併發標記（RowVersion）：資料庫每次都會改它，記錄它沒有意義。
    private static Dictionary<string, PropertyChange> GetChanges(EntityEntry entry)
    {
        return entry.Properties
            .Where(p => !p.Metadata.IsConcurrencyToken)
            .Where(p => entry.State != EntityState.Modified || p.IsModified)
            .ToDictionary(
                p => p.Metadata.Name,
                p => new PropertyChange(
                    entry.State == EntityState.Added ? null : p.OriginalValue,
                    entry.State == EntityState.Deleted ? null : p.CurrentValue));
    }

    private sealed record PropertyChange(object? Old, object? New);
}
