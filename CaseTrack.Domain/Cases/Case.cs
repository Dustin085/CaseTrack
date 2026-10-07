namespace CaseTrack.Domain.Cases;

public class Case
{
    private readonly List<SupplementRequest> _supplementRequests = new();
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string CaseNumber { get; private set; }
    public string Subject { get; private set; }
    public string Content { get; private set; }
    public CaseStatus Status { get; private set; } = CaseStatus.Submitted;
    public IReadOnlyCollection<SupplementRequest> SupplementRequests => _supplementRequests.AsReadOnly();
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

    public void RequestSupplement(string reason, DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureCanTransition(CaseStatus.AwaitingSupplement, CaseStatus.UnderReview);

        var supplementRequest = new SupplementRequest(_supplementRequests.Count + 1, reason, requestedAt);
        _supplementRequests.Add(supplementRequest);

        Status = CaseStatus.AwaitingSupplement;
    }

    public void SubmitSupplement(DateTimeOffset submittedAt)
    {
        EnsureCanTransition(CaseStatus.UnderReview, CaseStatus.AwaitingSupplement);

        _supplementRequests.Single(r => r.SubmittedAt is null).MarkSubmitted(submittedAt);

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
