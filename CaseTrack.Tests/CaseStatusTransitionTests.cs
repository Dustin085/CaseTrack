using CaseTrack.Domain.Cases;

namespace CaseTrack.Tests;

public class CaseStatusTransitionTests
{
    // ── 建立 ─────────────────────────────────────────────

    [Fact]
    public void Constructor_WhenCreated_StatusIsSubmitted()
    {
        var @case = CreateTestCase();

        Assert.Equal(CaseStatus.Submitted, @case.Status);
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
    public void RequestSupplement_WhenUnderReview_StatusIsAwaitingSupplement()
    {
        var @case = CreateCaseInStatus(CaseStatus.UnderReview);

        @case.RequestSupplement("Need more information.");

        Assert.Equal(CaseStatus.AwaitingSupplement, @case.Status);
        Assert.Equal("Need more information.", @case.SupplementReason);
    }

    [Fact]
    public void SubmitSupplement_WhenAwaitingSupplement_StatusIsUnderReview()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);

        @case.SubmitSupplement();

        Assert.Equal(CaseStatus.UnderReview, @case.Status);
    }

    // 補件可以來回多次；目前只保留最後一次的原因，之後重構成集合時要改這個測試
    [Fact]
    public void RequestSupplement_WhenRequestedAgainAfterSubmission_KeepsLatestReason()
    {
        var @case = CreateCaseInStatus(CaseStatus.AwaitingSupplement);
        @case.SubmitSupplement();

        @case.RequestSupplement("Still missing photos.");

        Assert.Equal(CaseStatus.AwaitingSupplement, @case.Status);
        Assert.Equal("Still missing photos.", @case.SupplementReason);
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
        AssertTransitionFails(from, CaseStatus.AwaitingSupplement, c => c.RequestSupplement("Reason"));
    }

    [Theory]
    [InlineData(CaseStatus.Submitted)]
    [InlineData(CaseStatus.UnderReview)]
    [InlineData(CaseStatus.Closed)]
    [InlineData(CaseStatus.Rejected)]
    public void SubmitSupplement_FromNonAwaitingSupplement_Throws(CaseStatus from)
    {
        AssertTransitionFails(from, CaseStatus.UnderReview, c => c.SubmitSupplement());
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
    public void RequestSupplement_WithBlankReason_ThrowsAndKeepsStatus(string? reason)
    {
        var @case = CreateCaseInStatus(CaseStatus.UnderReview);

        Assert.ThrowsAny<ArgumentException>(() => @case.RequestSupplement(reason!));
        Assert.Equal(CaseStatus.UnderReview, @case.Status);
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
                @case.RequestSupplement("Need more information.");
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
