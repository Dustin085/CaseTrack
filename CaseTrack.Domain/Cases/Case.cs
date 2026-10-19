namespace CaseTrack.Domain.Cases;

public class Case
{
    public static readonly TimeSpan SupplementDeadline = TimeSpan.FromDays(14);
    private readonly List<SupplementRequest> _supplementRequests = new();
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string CaseNumber { get; private set; }
    public string Subject { get; private set; }
    public string Content { get; private set; }
    public Petitioner Petitioner { get; private set; }
    public CaseStatus Status { get; private set; } = CaseStatus.Submitted;
    public IReadOnlyCollection<SupplementRequest> SupplementRequests => _supplementRequests.AsReadOnly();
    public string? RejectionReason { get; private set; }
    public Guid? DepartmentId { get; private set; } // null = 待分派
    public string? ClosingReply { get; private set; } // 結案回覆，還沒結案時是 null
    public string SubmittedBy { get; private set; }   // 送件民眾的使用者 Id（token 的 sub）
    public DateTimeOffset SubmittedAt { get; private set; }

    // 案件編號（每日流水號）需要查資料庫才能保證不重複，由外部產生後傳入。
    // 送件時間也由外部傳入（Handler 用 TimeProvider），Domain 不自己取時間
    public Case(string caseNumber, string subject, string content, Petitioner petitioner,
        string submittedBy, DateTimeOffset submittedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentNullException.ThrowIfNull(petitioner);
        ArgumentException.ThrowIfNullOrWhiteSpace(submittedBy);

        CaseNumber = caseNumber;
        Subject = subject;
        Content = content;
        Petitioner = petitioner;
        SubmittedBy = submittedBy;
        SubmittedAt = submittedAt;
    }

    // 給 EF Core 從資料庫讀出時使用：EF 沒辦法把值物件（Petitioner）綁定到建構子參數，
    // 所以改用這個建構子建立物件，再直接把資料庫的值寫進屬性。業務程式碼不能呼叫（private）。
    private Case()
    {
        CaseNumber = null!;
        Subject = null!;
        Content = null!;
        Petitioner = null!;
        SubmittedBy = null!;
    }

    public void StartReview()
    {
        EnsureCanTransition(CaseStatus.UnderReview, CaseStatus.Submitted);
        if (DepartmentId is null)
        {
            throw new CaseNotAssignedException();
        }
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

    public void SubmitSupplement(string content, DateTimeOffset submittedAt)
    {
        EnsureCanTransition(CaseStatus.UnderReview, CaseStatus.AwaitingSupplement);

        _supplementRequests.Single(r => r.SubmittedAt is null).MarkSubmitted(content, submittedAt);

        Status = CaseStatus.UnderReview;
    }

    public void Close(string reply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reply);
        EnsureCanTransition(CaseStatus.Closed, CaseStatus.UnderReview);

        ClosingReply = reply;
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

    public bool IsSupplementOverdue(DateTimeOffset now)
    {
        if (Status != CaseStatus.AwaitingSupplement)
        {
            return false;
        }
        var pendingRequest = _supplementRequests.Single(r => r.SubmittedAt is null);
        return now - pendingRequest.RequestedAt >= SupplementDeadline;
    }

    // 不予受理：分派人員一收到案件就判斷不屬本府權責、內容無意義等。
    // 跟承辦人的「退件」是不同的業務動作：只能在分派之前（已送出）做，不需要先分派。
    // 結果一樣是已退件；誰做的由稽核紀錄的 ChangedBy 區分
    public void Dismiss(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureCanTransition(CaseStatus.Rejected, CaseStatus.Submitted);

        RejectionReason = reason;
        Status = CaseStatus.Rejected;
    }

    public void RejectOverdueSupplement(DateTimeOffset now)
    {
        if (!IsSupplementOverdue(now))
        {
            throw new InvalidOperationException("沒有逾期的補件");
        }
        Reject("逾期未補件");
    }

    // 分派不改變狀態；已送出的案件可以重新分派（分派人員發現分錯了直接改），受理之後要換單位是「轉派」
    public void AssignTo(Guid departmentId)
    {
        // Guid 的「沒有值」是 Guid.Empty，不是空字串：換成 Guid 之後，原本針對字串的空白檢查不會自動轉換過來
        if (departmentId == Guid.Empty)
        {
            throw new ArgumentException("單位 Id 不能是空的", nameof(departmentId));
        }
        if (Status != CaseStatus.Submitted)
        {
            throw new CaseAssignmentNotAllowedException(Status);
        }
        DepartmentId = departmentId;
    }

    private void EnsureCanTransition(CaseStatus target, params CaseStatus[] allowedFrom)
    {
        if (!allowedFrom.Contains(Status))
        {
            throw new InvalidCaseStatusTransitionException(Status, target);
        }
    }
}
