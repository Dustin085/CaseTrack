using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Privacy;
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

    // 同步和非同步是兩個獨立的覆寫點：只覆寫其中一個，呼叫另一個版本時稽核會被靜靜跳過
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is { } dbContext)
        {
            AddAuditEntries(dbContext);
        }

        return base.SavingChanges(eventData, result);
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
        var changedBy = _currentUser.UserId;
        var changedAt = _timeProvider.GetUtcNow();

        // 先用 ToList() 固定下來：等一下要把 AuditEntry 加進同一個 DbContext，
        // 一邊列舉變更追蹤器、一邊新增，會修改正在列舉的集合
        var auditEntries = dbContext.ChangeTracker.Entries()
            // 紀錄本身不再稽核：稽核紀錄和個資查詢紀錄只能新增，再記一份只是重複
            .Where(entry => entry.Entity is not (AuditEntry or PersonalDataAccessEntry))
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
    // 個資欄位（Configuration 裡標了 IsPersonalData）只記錄遮罩後的值，稽核表不能變成個資外洩的管道。
    private static Dictionary<string, PropertyChange> GetChanges(EntityEntry entry)
    {
        return GetAuditableProperties(entry)
            .Where(p => !p.Entry.Metadata.IsConcurrencyToken)
            .Where(p => entry.State != EntityState.Modified || p.Entry.IsModified)
            .ToDictionary(
                p => p.Name,
                p => new PropertyChange(
                    entry.State == EntityState.Added ? null : ToAuditValue(p.Entry, p.Entry.OriginalValue),
                    entry.State == EntityState.Deleted ? null : ToAuditValue(p.Entry, p.Entry.CurrentValue)));
    }

    private static object? ToAuditValue(PropertyEntry property, object? value)
    {
        // null 要原樣保留：Convert.ToString(null) 會回傳 ""，「沒填」就會被記錄成「填了空字串」
        if (value is null || !property.Metadata.IsPersonalData())
        {
            return value;
        }

        // 不只處理 string：之後如果把非字串的欄位標成個資（例如生日），也一定會被遮罩，不會原樣寫進去
        return PersonalDataMasker.Mask(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    // 一般欄位在 entry.Properties；值物件（Complex Type，例如 Petitioner）的欄位在 entry.ComplexProperties，要另外展開。
    // 值物件的欄位名稱加上前綴（Petitioner.Phone），避免跟 entity 自己的欄位撞名。
    // 限制：只展開一層，值物件裡面如果還有值物件（巢狀），裡面那層不會被記錄。
    private static IEnumerable<(string Name, PropertyEntry Entry)> GetAuditableProperties(EntityEntry entry)
    {
        var properties = entry.Properties
            .Select(p => (p.Metadata.Name, p));

        var complexProperties = entry.ComplexProperties
            .SelectMany(complex => complex.Properties
                .Select(p => ($"{complex.Metadata.Name}.{p.Metadata.Name}", p)));

        return properties.Concat(complexProperties);
    }

    private sealed record PropertyChange(object? Old, object? New);
}
