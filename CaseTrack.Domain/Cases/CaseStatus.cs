namespace CaseTrack.Domain.Cases;

public enum CaseStatus
{
    Submitted,
    UnderReview,
    AwaitingSupplement,
    // 承辦人送出結案，等主管核定。資料庫存名稱（字串），所以插在中間不影響舊資料
    PendingApproval,
    Closed,
    Rejected,
}
