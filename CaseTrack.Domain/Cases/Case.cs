namespace CaseTrack.Domain.Cases;

public class Case
{
    public static readonly TimeSpan SupplementDeadline = TimeSpan.FromDays(14);
    private readonly List<SupplementRequest> _supplementRequests = new();
    private readonly List<Attachment> _attachments = new();
    public Guid Id { get; private set; } = Guid.NewGuid();
    // 存成字串：列表的關鍵字搜尋要對它做 LIKE。屬性改成 CaseNumber 型別（Value Converter）的話，
    // EF 沒辦法把 c.CaseNumber.Value.Contains(...) 翻譯成 SQL。格式由建構子收 CaseNumber 來保證
    public string CaseNumber { get; private set; }
    public string Subject { get; private set; }
    public string Content { get; private set; }
    public Petitioner Petitioner { get; private set; }
    public CaseStatus Status { get; private set; } = CaseStatus.Submitted;
    public IReadOnlyCollection<SupplementRequest> SupplementRequests => _supplementRequests.AsReadOnly();
    // 送件與每次補件的附件都在這一個集合（用 SupplementRequestId 區分）：權限的單位是案件，下載時只要查一個地方
    public IReadOnlyCollection<Attachment> Attachments => _attachments.AsReadOnly();
    public string? RejectionReason { get; private set; }
    public Guid? DepartmentId { get; private set; } // null = 待分派
    // 結案回覆：送出結案時寫入，主管核定前民眾看不到（由 Application 的民眾 DTO 過濾）；被主管退回時清掉
    public string? ClosingReply { get; private set; }
    public string? ClosingSubmittedBy { get; private set; } // 送出結案的人（token 的 sub），用來擋自己核定自己
    public string? ClosingReturnReason { get; private set; } // 主管最近一次退回的原因，再次送出結案時清掉
    public string SubmittedBy { get; private set; }   // 送件民眾的使用者 Id（token 的 sub）
    public DateTimeOffset SubmittedAt { get; private set; }

    // 每個動作在目前的狀態下能不能做。
    // 動作本身和 allowedActions 都讀這裡，規則只寫一次；一致性測試保證兩者不會不同步
    private bool IsAwaitingReview => Status == CaseStatus.Submitted;
    public bool CanStartReview => IsAwaitingReview && DepartmentId is not null;
    public bool CanRequestSupplement => Status == CaseStatus.UnderReview;
    public bool CanSubmitForClosing => Status == CaseStatus.UnderReview;
    public bool CanApproveClosing => Status == CaseStatus.PendingApproval;
    public bool CanReturnClosing => Status == CaseStatus.PendingApproval;
    public bool CanReject => Status is CaseStatus.Submitted
        or CaseStatus.UnderReview
        or CaseStatus.AwaitingSupplement;
    public bool CanDismiss => Status == CaseStatus.Submitted;
    public bool CanSubmitSupplement => Status == CaseStatus.AwaitingSupplement;
    public bool CanAssign => Status == CaseStatus.Submitted;

    // 案件編號的流水號需要查資料庫才能保證不重複，由外部產生後傳入；收 CaseNumber 不收 string，格式一定正確。
    // 送件時間也由外部傳入（Handler 用 TimeProvider），Domain 不自己取時間。
    // 建立後 Attachments 全部都是這次新增的，順序和傳入的 attachments 相同（Application 用它對應要存的檔案）
    public Case(CaseNumber caseNumber, string subject, string content, Petitioner petitioner,
        string submittedBy, DateTimeOffset submittedAt, IReadOnlyList<NewAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(caseNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentNullException.ThrowIfNull(petitioner);
        ArgumentException.ThrowIfNullOrWhiteSpace(submittedBy);
        AttachmentRules.Validate(attachments);

        CaseNumber = caseNumber.Value;
        Subject = subject;
        Content = content;
        Petitioner = petitioner;
        SubmittedBy = submittedBy;
        SubmittedAt = submittedAt;
        AddAttachments(null, attachments, submittedAt);
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
        if (!IsAwaitingReview)
        {
            throw new InvalidCaseStatusTransitionException(Status, CaseStatus.UnderReview);
        }
        if (DepartmentId is null)
        {
            throw new CaseNotAssignedException();
        }
        Status = CaseStatus.UnderReview;
    }

    public void RequestSupplement(string reason, DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!CanRequestSupplement)
        {
            throw new InvalidCaseStatusTransitionException(Status, CaseStatus.AwaitingSupplement);
        }

        var supplementRequest = new SupplementRequest(_supplementRequests.Count + 1, reason, requestedAt);
        _supplementRequests.Add(supplementRequest);

        Status = CaseStatus.AwaitingSupplement;
    }

    // 回傳這次新增的附件（順序和傳入的相同）：Application 要用它們的 Id 當檔名存檔
    public IReadOnlyList<Attachment> SubmitSupplement(string content, DateTimeOffset submittedAt,
        IReadOnlyList<NewAttachment> attachments)
    {
        // 全部檢查完才開始修改：附件不合法時，補件不能已經被標記成「已補件」（物件停在一半的狀態）
        if (!CanSubmitSupplement)
        {
            throw new InvalidCaseStatusTransitionException(Status, CaseStatus.UnderReview);
        }
        AttachmentRules.Validate(attachments);

        var pendingRequest = _supplementRequests.Single(r => r.SubmittedAt is null);
        pendingRequest.MarkSubmitted(content, submittedAt);
        var added = AddAttachments(pendingRequest.Id, attachments, submittedAt);

        Status = CaseStatus.UnderReview;
        return added;
    }

    // 呼叫前必須已經用 AttachmentRules.Validate 檢查過
    private IReadOnlyList<Attachment> AddAttachments(Guid? supplementRequestId,
        IReadOnlyList<NewAttachment> attachments, DateTimeOffset uploadedAt)
    {
        var added = attachments
            .Select(a => new Attachment(supplementRequestId, a.FileName, a.ContentType, a.Size, uploadedAt))
            .ToList();
        _attachments.AddRange(added);
        return added;
    }

    // 承辦人送出結案：不會直接結案，要等主管核定。
    // 待核定時不能退件、要求補件（CanReject 等不含這個狀態）：主管要先退回，案件回到審核中再處理
    public void SubmitForClosing(string reply, string submittedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reply);
        ArgumentException.ThrowIfNullOrWhiteSpace(submittedBy);
        if (!CanSubmitForClosing)
        {
            throw new InvalidCaseStatusTransitionException(Status, CaseStatus.PendingApproval);
        }

        ClosingReply = reply;
        ClosingSubmittedBy = submittedBy;
        ClosingReturnReason = null;
        Status = CaseStatus.PendingApproval;
    }

    // 主管核定：結案生效，民眾才看得到結案回覆。
    // 先檢查狀態再檢查是不是本人：狀態不對時，不管是誰都不能核定
    public void ApproveClosing(string approvedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);
        if (!CanApproveClosing)
        {
            throw new InvalidCaseStatusTransitionException(Status, CaseStatus.Closed);
        }

        if (approvedBy == ClosingSubmittedBy)
        {
            throw new SelfApprovalNotAllowedException();
        }

        Status = CaseStatus.Closed;
    }

    // 主管退回：回到審核中，承辦人修改後重新送出。
    // 清掉結案回覆，維持「ClosingReply 有值 = 待核定或已結案」；退回前的內容留在稽核紀錄裡
    public void ReturnClosing(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!CanReturnClosing)
        {
            throw new InvalidCaseStatusTransitionException(Status, CaseStatus.UnderReview);
        }

        ClosingReturnReason = reason;
        ClosingReply = null;
        ClosingSubmittedBy = null;
        Status = CaseStatus.UnderReview;
    }

    public void Reject(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!CanReject)
        {
            throw new InvalidCaseStatusTransitionException(Status, CaseStatus.Rejected);
        }

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
        if (!CanDismiss)
        {
            throw new InvalidCaseStatusTransitionException(Status, CaseStatus.Rejected);
        }

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
        if (!CanAssign)
        {
            throw new CaseAssignmentNotAllowedException(Status);
        }
        DepartmentId = departmentId;
    }
}
