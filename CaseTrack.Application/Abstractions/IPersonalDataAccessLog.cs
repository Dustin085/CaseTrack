namespace CaseTrack.Application.Abstractions;

// 個資查詢紀錄：誰、什麼時候、看了哪個案件的完整個資。
// 跟稽核紀錄（AuditEntries）分開：稽核記錄的是「資料的修改」，由攔截器自動產生；
// 這裡記錄的是「誰看了什麼」，由 Handler 明確寫入，常見的查詢是「某個人最近看了哪些民眾的資料」。
// Record 只是加進目前的工作單位，要呼叫 IUnitOfWork.SaveChangesAsync 才會寫入
public interface IPersonalDataAccessLog
{
    void Record(Guid caseId, string accessedBy, DateTimeOffset accessedAt);
}
