namespace CaseTrack.Infrastructure.Persistence.Auditing;

// 一筆資料異動的稽核紀錄，由 AuditInterceptor 在 SaveChanges 時自動產生，跟業務資料在同一個交易裡寫入。
// 稽核紀錄只能新增，不能修改：所有屬性都是 private set，也沒有任何修改的方法。
public sealed class AuditEntry
{
    public long Id { get; private set; }
    public string EntityType { get; private set; }
    public string EntityId { get; private set; }
    public string Action { get; private set; }
    public string Changes { get; private set; }
    public string ChangedBy { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    // 參數名稱跟屬性一致，EF 從資料庫讀出時也能透過這個建構子建立物件
    public AuditEntry(
        string entityType,
        string entityId,
        string action,
        string changes,
        string changedBy,
        DateTimeOffset changedAt)
    {
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        Changes = changes;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
    }
}
