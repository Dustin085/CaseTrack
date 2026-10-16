namespace CaseTrack.Infrastructure.Persistence.Auditing;

// 一筆個資查詢紀錄。跟 AuditEntry 一樣只能新增，不能修改。
// 刻意不對 Cases 設外鍵：紀錄要獨立保存，不能因為案件的資料變動而受影響
public sealed class PersonalDataAccessEntry
{
    public long Id { get; private set; }
    public Guid CaseId { get; private set; }
    public string AccessedBy { get; private set; }
    public DateTimeOffset AccessedAt { get; private set; }

    public PersonalDataAccessEntry(Guid caseId, string accessedBy, DateTimeOffset accessedAt)
    {
        CaseId = caseId;
        AccessedBy = accessedBy;
        AccessedAt = accessedAt;
    }
}
