using CaseTrack.Domain.Cases;

namespace CaseTrack.Tests;

public class CaseStatusTransitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(8));

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
        Assert.ThrowsAny<ArgumentException>(() => new Case(caseNumber, subject, content));
    }

    // ── 合法轉換 ─────────────────────────────────────────

    [Fact]
    public void StartReview_WhenSubmitted_StatusIsUnderReview()
    {
        var @case = CreateCaseInStatus(CaseStatus.Submitted);

        @case.StartReview();

        Assert.Equal(CaseStatus.UnderReview, @case.Status);
    }

    [Fact]
    public void Close_WhenUnderReview_StatusIsClosed()
    {
        var @case = CreateCaseInStatus(CaseStatus.UnderReview);

        @case.Close();

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

        @case.SubmitSupplement(Now.AddDays(1));

        Assert.Equal(CaseStatus.UnderReview, @case.Status);
        var request = Assert.Single(@case.SupplementRequests);
        Assert.Equal(Now.AddDays(1), request.SubmittedAt);
    }

    // 邊界值：同一時間補件是允許的（規則是「不能早於」）
    [Fact]
    public void SubmitSupplement_AtSameTimeAsRequested_Succeeds()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);

        @case.SubmitSupplement(Now);

        Assert.Equal(Now, Assert.Single(@case.SupplementRequests).SubmittedAt);
    }

    [Fact]
    public void SubmitSupplement_BeforeRequestedAt_ThrowsAndKeepsState()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);

        var exception = Assert.Throws<SubmittedAtBeforeRequestedAtException>(
            () => @case.SubmitSupplement(Now.AddMinutes(-1)));

        Assert.Equal(Now.AddMinutes(-1), exception.SubmittedAt);
        Assert.Equal(Now, exception.RequestedAt);
        Assert.Equal(CaseStatus.AwaitingSupplement, @case.Status);
        Assert.Null(Assert.Single(@case.SupplementRequests).SubmittedAt);
    }

    [Fact]
    public void RequestSupplement_WhenRequestedAgainAfterSubmission_KeepsAllRequests()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);
        @case.SubmitSupplement(Now.AddDays(1));

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
        @case.SubmitSupplement(Now.AddDays(1));
        @case.RequestSupplement("Still missing photos.", Now.AddDays(2));

        @case.SubmitSupplement(Now.AddDays(3));

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
        AssertTransitionFails(from, CaseStatus.UnderReview, c => c.SubmitSupplement(Now));
    }

    [Theory]
    [InlineData(CaseStatus.Submitted)]
    [InlineData(CaseStatus.AwaitingSupplement)]
    [InlineData(CaseStatus.Closed)]
    [InlineData(CaseStatus.Rejected)]
    public void Close_FromNonUnderReview_Throws(CaseStatus from)
    {
        AssertTransitionFails(from, CaseStatus.Closed, c => c.Close());
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

    // ── Helpers ──────────────────────────────────────────

    private static Case CreateTestCase()
    {
        return new Case("20261005-0001", "Test Subject", "Test Content");
    }

    private static Case CreateCaseInStatus(CaseStatus status)
    {
        var @case = CreateTestCase();

        switch (status)
        {
            case CaseStatus.Submitted:
                break;
            case CaseStatus.UnderReview:
                @case.StartReview();
                break;
            case CaseStatus.AwaitingSupplement:
                @case.StartReview();
                @case.RequestSupplement("Need more information.", Now);
                break;
            case CaseStatus.Closed:
                @case.StartReview();
                @case.Close();
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
