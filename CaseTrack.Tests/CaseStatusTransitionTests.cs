using CaseTrack.Domain.Cases;
using CaseTrack.Domain.Departments;

namespace CaseTrack.Tests;

public class CaseStatusTransitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly Petitioner Petitioner = new("王小明", "0912345678", null, null);
    private const string SubmittedBy = "u-citizen-1";
    private static readonly Department Department = new("001", "Test");

    // ── 建立 ─────────────────────────────────────────────

    [Fact]
    public void Constructor_WhenCreated_StatusIsSubmitted()
    {
        var @case = CreateTestCase();

        Assert.Equal(CaseStatus.Submitted, @case.Status);
        Assert.Empty(@case.SupplementRequests);
    }

    // 這裡要測的就是參數本身，所以直接 new，不藏進 helper
    [Theory]
    [InlineData("", "Subject", "Content")]
    [InlineData("20261005-0001", " ", "Content")]
    [InlineData("20261005-0001", "Subject", "")]
    public void Constructor_WithBlankArgument_ThrowsArgumentException(
        string caseNumber, string subject, string content)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Case(caseNumber, subject, content, Petitioner, SubmittedBy, Now, []));
    }

    [Fact]
    public void Constructor_WithNullPetitioner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new Case("20261005-0001", "Subject", "Content", null!, SubmittedBy, Now, []));
    }

    // 送件人一定要有：民眾要登入才能陳情，案件一定知道是誰送的
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankSubmittedBy_ThrowsArgumentException(string? submittedBy)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new Case("20261005-0001", "Subject", "Content", Petitioner, submittedBy!, Now, []));
    }

    [Fact]
    public void Constructor_WhenCreated_RecordsSubmitterAndTime()
    {
        var @case = CreateTestCase();

        Assert.Equal(SubmittedBy, @case.SubmittedBy);
        Assert.Equal(Now, @case.SubmittedAt);
    }

    // ── 合法轉換 ─────────────────────────────────────────

    [Fact]
    public void StartReview_WhenSubmitted_StatusIsUnderReview()
    {
        var @case = CreateCaseInStatus(CaseStatus.Submitted);
        @case.AssignTo(Department.Id);

        @case.StartReview();

        Assert.Equal(CaseStatus.UnderReview, @case.Status);
    }

    [Fact]
    public void Close_WhenUnderReview_StatusIsClosed()
    {
        var @case = CreateCaseInStatus(CaseStatus.UnderReview);

        @case.Close("Test Reply");

        Assert.Equal("Test Reply", @case.ClosingReply);
        Assert.Equal(CaseStatus.Closed, @case.Status);
    }

    [Theory]
    [InlineData(CaseStatus.Submitted)]
    [InlineData(CaseStatus.UnderReview)]
    [InlineData(CaseStatus.AwaitingSupplement)]
    public void Reject_FromRejectableStatus_StatusIsRejected(CaseStatus from)
    {
        var @case = CreateCaseInStatus(from);

        @case.Reject("Not our department.");

        Assert.Equal(CaseStatus.Rejected, @case.Status);
        Assert.Equal("Not our department.", @case.RejectionReason);
    }

    // ── 補件紀錄 ─────────────────────────────────────────

    [Fact]
    public void RequestSupplement_WhenUnderReview_AddsOpenRequest()
    {
        var @case = CreateCaseInStatus(CaseStatus.UnderReview);

        @case.RequestSupplement("Need more information.", Now);

        Assert.Equal(CaseStatus.AwaitingSupplement, @case.Status);
        var request = Assert.Single(@case.SupplementRequests);
        Assert.Equal(1, request.Sequence);
        Assert.Equal("Need more information.", request.Reason);
        Assert.Equal(Now, request.RequestedAt);
        Assert.Null(request.SubmittedAt);
    }

    [Fact]
    public void SubmitSupplement_WhenAwaitingSupplement_MarksRequestSubmitted()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);

        @case.SubmitSupplement("Test Content", Now.AddDays(1), []);

        Assert.Equal(CaseStatus.UnderReview, @case.Status);
        var request = Assert.Single(@case.SupplementRequests);
        Assert.Equal(Now.AddDays(1), request.SubmittedAt);
        Assert.Equal("Test Content", request.SubmittedContent);
    }

    // 邊界值：同一時間補件是允許的（規則是「不能早於」）
    [Fact]
    public void SubmitSupplement_AtSameTimeAsRequested_Succeeds()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);

        @case.SubmitSupplement("Test Content", Now, []);

        Assert.Equal(Now, Assert.Single(@case.SupplementRequests).SubmittedAt);
    }

    [Fact]
    public void SubmitSupplement_BeforeRequestedAt_ThrowsAndKeepsState()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);

        var exception = Assert.Throws<SubmittedAtBeforeRequestedAtException>(
            () => @case.SubmitSupplement("Test Content", Now.AddMinutes(-1), []));

        Assert.Equal(Now.AddMinutes(-1), exception.SubmittedAt);
        Assert.Equal(Now, exception.RequestedAt);
        Assert.Equal(CaseStatus.AwaitingSupplement, @case.Status);
        Assert.Null(Assert.Single(@case.SupplementRequests).SubmittedAt);
    }

    [Fact]
    public void RequestSupplement_WhenRequestedAgainAfterSubmission_KeepsAllRequests()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);
        @case.SubmitSupplement("Test Content", Now.AddDays(1), []);

        @case.RequestSupplement("Still missing photos.", Now.AddDays(2));

        Assert.Equal(CaseStatus.AwaitingSupplement, @case.Status);
        Assert.Collection(@case.SupplementRequests,
            first =>
            {
                Assert.Equal(1, first.Sequence);
                Assert.Equal("Need more information.", first.Reason);
                Assert.Equal(Now.AddDays(1), first.SubmittedAt);
            },
            second =>
            {
                Assert.Equal(2, second.Sequence);
                Assert.Equal("Still missing photos.", second.Reason);
                Assert.Equal(Now.AddDays(2), second.RequestedAt);
                Assert.Null(second.SubmittedAt);
            });
    }

    [Fact]
    public void SubmitSupplement_WhenMultipleRequests_MarksLatestOnly()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);
        @case.SubmitSupplement("Test Content", Now.AddDays(1), []);
        @case.RequestSupplement("Still missing photos.", Now.AddDays(2));

        @case.SubmitSupplement("Test Content", Now.AddDays(3), []);

        Assert.Collection(@case.SupplementRequests,
            first => Assert.Equal(Now.AddDays(1), first.SubmittedAt),
            second => Assert.Equal(Now.AddDays(3), second.SubmittedAt));
    }

    // 補件中被退件：最後一筆維持未補件，正好記錄「民眾沒補」
    [Fact]
    public void Reject_WhenAwaitingSupplement_LeavesRequestUnsubmitted()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);

        @case.Reject("No supplement received.");

        Assert.Null(Assert.Single(@case.SupplementRequests).SubmittedAt);
    }

    // 外部拿到的集合不能被轉型回 List 來偷加資料
    [Fact]
    public void SupplementRequests_WhenExposed_CannotBeCastToList()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);

        Assert.False(@case.SupplementRequests is List<SupplementRequest>);
    }

    // ── 非法轉換 ─────────────────────────────────────────

    [Theory]
    [InlineData(CaseStatus.UnderReview)]
    [InlineData(CaseStatus.AwaitingSupplement)]
    [InlineData(CaseStatus.Closed)]
    [InlineData(CaseStatus.Rejected)]
    public void StartReview_FromNonSubmitted_Throws(CaseStatus from)
    {
        AssertTransitionFails(from, CaseStatus.UnderReview, c => c.StartReview());
    }

    [Theory]
    [InlineData(CaseStatus.Submitted)]
    [InlineData(CaseStatus.AwaitingSupplement)]
    [InlineData(CaseStatus.Closed)]
    [InlineData(CaseStatus.Rejected)]
    public void RequestSupplement_FromNonUnderReview_Throws(CaseStatus from)
    {
        AssertTransitionFails(from, CaseStatus.AwaitingSupplement, c => c.RequestSupplement("Reason", Now));
    }

    [Theory]
    [InlineData(CaseStatus.Submitted)]
    [InlineData(CaseStatus.UnderReview)]
    [InlineData(CaseStatus.Closed)]
    [InlineData(CaseStatus.Rejected)]
    public void SubmitSupplement_FromNonAwaitingSupplement_Throws(CaseStatus from)
    {
        AssertTransitionFails(from, CaseStatus.UnderReview, c => c.SubmitSupplement("Test Content", Now, []));
    }

    [Theory]
    [InlineData(CaseStatus.Submitted)]
    [InlineData(CaseStatus.AwaitingSupplement)]
    [InlineData(CaseStatus.Closed)]
    [InlineData(CaseStatus.Rejected)]
    public void Close_FromNonUnderReview_Throws(CaseStatus from)
    {
        AssertTransitionFails(from, CaseStatus.Closed, c => c.Close("Test Reply"));
    }

    [Theory]
    [InlineData(CaseStatus.Closed)]
    [InlineData(CaseStatus.Rejected)]
    public void Reject_FromNonRejectableStatus_Throws(CaseStatus from)
    {
        AssertTransitionFails(from, CaseStatus.Rejected, c => c.Reject("Reason"));
    }

    // ── 原因空白 ─────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestSupplement_WithBlankReason_ThrowsAndKeepsState(string? reason)
    {
        var @case = CreateCaseInStatus(CaseStatus.UnderReview);

        Assert.ThrowsAny<ArgumentException>(() => @case.RequestSupplement(reason!, Now));
        Assert.Equal(CaseStatus.UnderReview, @case.Status);
        Assert.Empty(@case.SupplementRequests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithBlankReason_ThrowsAndKeepsStatus(string? reason)
    {
        var @case = CreateCaseInStatus(CaseStatus.Submitted);

        Assert.ThrowsAny<ArgumentException>(() => @case.Reject(reason!));
        Assert.Equal(CaseStatus.Submitted, @case.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Close_WithBlankReply_ThrowsAndKeepStatus(string? reply)
    {
        var @case = CreateCaseInStatus(CaseStatus.UnderReview);

        Assert.ThrowsAny<ArgumentException>(() => @case.Close(reply!));
        Assert.Equal(CaseStatus.UnderReview, @case.Status);
        Assert.Null(@case.ClosingReply);
    }

    // 補件說明的檢查在 SupplementRequest.MarkSubmitted 裡（規則跟著資料走），
    // 失敗時案件和那一筆補件紀錄都不能被改到一半
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SubmitSupplement_WithBlankContent_ThrowsAndKeepsRequestOpen(string? content)
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);

        Assert.ThrowsAny<ArgumentException>(() => @case.SubmitSupplement(content!, Now.AddDays(1), []));
        Assert.Equal(CaseStatus.AwaitingSupplement, @case.Status);
        var request = Assert.Single(@case.SupplementRequests);
        Assert.Null(request.SubmittedAt);
        Assert.Null(request.SubmittedContent);
    }

    // ── 逾期未補件 ─────────────────────────────────────────
    // 業務規則：要求補件後 14 天未補即逾期（2026-10-12 定案）
    [Fact]
    public void SupplementDeadline_Is14Days()
    {
        Assert.Equal(TimeSpan.FromDays(14), Case.SupplementDeadline);
    }

    [Theory]
    [InlineData(0)] // 剛好
    [InlineData(1440)] // 多 1 天
    public void IsSupplementOverdue_AtOrAfterDeadline_ReturnsTrue(int minutesAfterDeadline)
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);
        var now = Now + Case.SupplementDeadline + TimeSpan.FromMinutes(minutesAfterDeadline);

        Assert.True(@case.IsSupplementOverdue(now));
    }

    [Theory]
    [InlineData(-1)] // 還差 1 分鐘
    [InlineData(-1440)] // 還差 1 天
    public void IsSupplementOverdue_BeforeDeadline_ReturnsFalse(int minutesAfterDeadline)
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);
        var now = Now + Case.SupplementDeadline + TimeSpan.FromMinutes(minutesAfterDeadline);

        Assert.False(@case.IsSupplementOverdue(now));
    }

    [Fact]
    public void IsSupplementOverdue_WhenSecondSupplementRequestBeforeDeadline_ReturnsFalse()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);
        @case.SubmitSupplement("Test Content", Now + TimeSpan.FromDays(1), []);
        @case.RequestSupplement("Still missing photos.", Now + TimeSpan.FromDays(2));

        Assert.False(@case.IsSupplementOverdue(Now + TimeSpan.FromDays(15)));
    }

    [Theory]
    [InlineData(CaseStatus.Rejected)]
    [InlineData(CaseStatus.UnderReview)]
    public void IsSupplementOverdue_CaseNotAwaitingSupplement_ReturnsFalse(CaseStatus status)
    {
        var @case = CreateCaseInStatus(status);

        Assert.False(@case.IsSupplementOverdue(Now));
    }

    [Fact]
    public void RejectOverdueSupplement_WhenOverdue_ChangesStatusToRejected()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);
        var now = Now + Case.SupplementDeadline + TimeSpan.FromDays(1);

        @case.RejectOverdueSupplement(now);

        Assert.Equal(CaseStatus.Rejected, @case.Status);
        Assert.Equal("逾期未補件", @case.RejectionReason);
    }

    [Fact]
    public void RejectOverdueSupplement_WhenNotOverdue_ThrowsAndKeepsState()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);
        var now = Now + Case.SupplementDeadline + TimeSpan.FromDays(-1);

        Assert.Throws<InvalidOperationException>(() => @case.RejectOverdueSupplement(now));
        Assert.Equal(CaseStatus.AwaitingSupplement, @case.Status);
    }

    // ── Department ───────────────────────────────────────
    [Fact]
    public void Constructor_WhenCreated_DepartmentIdIsNull()
    {
        var @case = CreateTestCase();

        Assert.Null(@case.DepartmentId);
    }

    [Fact]
    public void AssignTo_SubmittedCase_DepartmentIdSet()
    {
        var @case = CreateTestCase();

        @case.AssignTo(Department.Id);

        Assert.Equal(CaseStatus.Submitted, @case.Status);
        Assert.Equal(Department.Id, @case.DepartmentId);
    }

    [Fact]
    public void AssignTo_AssignedCase_DepartmentIdChanged()
    {
        var @case = CreateTestCase();
        @case.AssignTo(Department.Id);
        var itDepartment = new Department("002", "IT department");

        @case.AssignTo(itDepartment.Id);

        Assert.Equal(itDepartment.Id, @case.DepartmentId);
    }

    [Theory]
    [InlineData(CaseStatus.UnderReview)]
    [InlineData(CaseStatus.AwaitingSupplement)]
    [InlineData(CaseStatus.Closed)]
    [InlineData(CaseStatus.Rejected)]
    public void AssignTo_WhenNotSubmitted_ThrowsCaseAssignmentNotAllowed(CaseStatus status)
    {
        var @case = CreateCaseInStatus(status);

        var exception = Assert.Throws<CaseAssignmentNotAllowedException>(() => @case.AssignTo(Department.Id));
        Assert.Equal(status, exception.CurrentStatus);
    }

    [Fact]
    public void AssignTo_EmptyDepartmentId_ThrowsArgumentException()
    {
        var @case = CreateTestCase();

        Assert.Throws<ArgumentException>(() => @case.AssignTo(Guid.Empty));
        Assert.Null(@case.DepartmentId);
    }

    [Fact]
    public void StartReview_WhenNotAssigned_ThrowsCaseNotAssigned()
    {
        var @case = CreateTestCase();

        Assert.Throws<CaseNotAssignedException>(@case.StartReview);
        Assert.Equal(CaseStatus.Submitted, @case.Status);
    }

    // 釘住檢查順序：先檢查狀態，再檢查有沒有分派。
    // 已退件的案件就算分派了也不能受理，先回報「已退件」，使用者才不會白跑一趟去分派
    [Fact]
    public void StartReview_WhenRejectedAndNotAssigned_ThrowsInvalidTransitionFirst()
    {
        var @case = CreateCaseInStatus(CaseStatus.Rejected);
        Assert.Null(@case.DepartmentId);

        Assert.Throws<InvalidCaseStatusTransitionException>(@case.StartReview);
    }

    // ── 不予受理 ─────────────────────────────────────────

    // 分派人員一收到就不受理：案件還沒分派也可以
    [Fact]
    public void Dismiss_WhenSubmittedAndNotAssigned_StatusIsRejectedWithReason()
    {
        var @case = CreateTestCase();

        @case.Dismiss("非本府權責");

        Assert.Equal(CaseStatus.Rejected, @case.Status);
        Assert.Equal("非本府權責", @case.RejectionReason);
    }

    // 已經分派、但還沒受理：分派人員仍然可以改判不受理
    [Fact]
    public void Dismiss_WhenSubmittedAndAssigned_StatusIsRejected()
    {
        var @case = CreateTestCase();
        @case.AssignTo(Department.Id);

        @case.Dismiss("非本府權責");

        Assert.Equal(CaseStatus.Rejected, @case.Status);
    }

    // 受理之後就由承辦單位負責，要退件是承辦人的「退件」，不是分派人員的「不予受理」
    [Theory]
    [InlineData(CaseStatus.UnderReview)]
    [InlineData(CaseStatus.AwaitingSupplement)]
    [InlineData(CaseStatus.Closed)]
    [InlineData(CaseStatus.Rejected)]
    public void Dismiss_WhenNotSubmitted_ThrowsAndKeepsStatus(CaseStatus status)
    {
        var @case = CreateCaseInStatus(status);

        var exception = Assert.Throws<InvalidCaseStatusTransitionException>(() => @case.Dismiss("非本府權責"));
        Assert.Equal(status, exception.CurrentStatus);
        Assert.Equal(status, @case.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Dismiss_WithBlankReason_ThrowsAndKeepsStatus(string? reason)
    {
        var @case = CreateTestCase();

        Assert.ThrowsAny<ArgumentException>(() => @case.Dismiss(reason!));
        Assert.Equal(CaseStatus.Submitted, @case.Status);
        Assert.Null(@case.RejectionReason);
    }

    // ── Helpers ──────────────────────────────────────────

    private static Case CreateTestCase()
    {
        return new Case("20261005-0001", "Test Subject", "Test Content", Petitioner, SubmittedBy, Now, []);
    }

    // 受理之前一定要先分派
    private static void AssignAndStartReview(Case @case)
    {
        @case.AssignTo(Department.Id);
        @case.StartReview();
    }

    private static Case CreateCaseInStatus(CaseStatus status)
    {
        var @case = CreateTestCase();

        switch (status)
        {
            case CaseStatus.Submitted:
                break;
            case CaseStatus.UnderReview:
                AssignAndStartReview(@case);
                break;
            case CaseStatus.AwaitingSupplement:
                AssignAndStartReview(@case);
                @case.RequestSupplement("Need more information.", Now);
                break;
            case CaseStatus.Closed:
                AssignAndStartReview(@case);
                @case.Close("Test Reply");
                break;
            case CaseStatus.Rejected:
                @case.Reject("Test Rejection");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        return @case;
    }

    // 非法轉換要同時確認：丟對的例外、例外帶對的狀態、案件狀態沒有被改到
    private static void AssertTransitionFails(CaseStatus from, CaseStatus target, Action<Case> transition)
    {
        var @case = CreateCaseInStatus(from);

        var exception = Assert.Throws<InvalidCaseStatusTransitionException>(() => transition(@case));

        Assert.Equal(from, exception.CurrentStatus);
        Assert.Equal(target, exception.TargetStatus);
        Assert.Equal(from, @case.Status);
    }
}
