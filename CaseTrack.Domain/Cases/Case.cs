namespace CaseTrack.Domain.Cases;

public class Case
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string CaseNumber { get; private set; }
    public string Subject { get; private set; }
    public string Content { get; private set; }
    public CaseStatus Status { get; private set; } = CaseStatus.Submitted;

    // 先用單一欄位；補件會來回多次，之後重構成 SupplementRequest 集合保留每一次的紀錄
    public string? SupplementReason { get; private set; }
    public string? RejectionReason { get; private set; }

    // 案件編號（每日流水號）需要查資料庫才能保證不重複，由外部產生後傳入
    public Case(string caseNumber, string subject, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        CaseNumber = caseNumber;
        Subject = subject;
        Content = content;
    }

    public void StartReview()
    {
        EnsureCanTransition(CaseStatus.UnderReview, CaseStatus.Submitted);
        Status = CaseStatus.UnderReview;
    }

    public void RequestSupplement(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureCanTransition(CaseStatus.AwaitingSupplement, CaseStatus.UnderReview);

        SupplementReason = reason;
        Status = CaseStatus.AwaitingSupplement;
    }

    public void SubmitSupplement()
    {
        EnsureCanTransition(CaseStatus.UnderReview, CaseStatus.AwaitingSupplement);
        Status = CaseStatus.UnderReview;
    }

    public void Close()
    {
        EnsureCanTransition(CaseStatus.Closed, CaseStatus.UnderReview);
        Status = CaseStatus.Closed;
    }

    public void Reject(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureCanTransition(
            CaseStatus.Rejected,
            CaseStatus.Submitted, CaseStatus.UnderReview, CaseStatus.AwaitingSupplement);

        RejectionReason = reason;
        Status = CaseStatus.Rejected;
    }

    private void EnsureCanTransition(CaseStatus target, params CaseStatus[] allowedFrom)
    {
        if (!allowedFrom.Contains(Status))
        {
            throw new InvalidCaseStatusTransitionException(Status, target);
        }
    }
}
