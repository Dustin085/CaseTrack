using CaseTrack.Domain.Cases;
using CaseTrack.Domain.Departments;

namespace CaseTrack.Tests;

public class CaseAttachmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly Petitioner Petitioner = new("王小明", "0912345678", null, null);
    private const string SubmittedBy = "u-citizen-1";
    private static readonly Department Department = new("001", "Test");

    // ── 送件 ─────────────────────────────────────────────

    [Fact]
    public void Constructor_WithAttachments_AddsThemInOrderWithoutSupplementRequest()
    {
        var @case = CreateCase([Jpg("現場.jpg"), Pdf("說明.pdf")]);

        // 順序和傳入的相同：Application 靠這個把附件和要存的檔案一一對應
        Assert.Collection(@case.Attachments,
            a =>
            {
                Assert.Equal("現場.jpg", a.FileName);
                Assert.Equal("image/jpeg", a.ContentType);
                Assert.Equal(1024, a.Size);
                Assert.Null(a.SupplementRequestId);
                Assert.Equal(Now, a.UploadedAt);
            },
            a => Assert.Equal("說明.pdf", a.FileName));
        Assert.NotEqual(@case.Attachments.First().Id, @case.Attachments.Last().Id);
    }

    [Fact]
    public void Constructor_WithoutAttachments_HasNone()
    {
        var @case = CreateCase([]);

        Assert.Empty(@case.Attachments);
    }

    [Fact]
    public void Constructor_WithNullAttachments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CreateCase(null!));
    }

    // ── 規則：數量 ───────────────────────────────────────

    [Fact]
    public void Constructor_WithMaxCount_Succeeds()
    {
        var attachments = Enumerable.Range(1, AttachmentRules.MaxCountPerSubmission)
            .Select(i => Jpg($"{i}.jpg")).ToList();

        var @case = CreateCase(attachments);

        Assert.Equal(AttachmentRules.MaxCountPerSubmission, @case.Attachments.Count);
    }

    [Fact]
    public void Constructor_WithMoreThanMaxCount_Throws()
    {
        var attachments = Enumerable.Range(1, AttachmentRules.MaxCountPerSubmission + 1)
            .Select(i => Jpg($"{i}.jpg")).ToList();

        Assert.Throws<InvalidAttachmentException>(() => CreateCase(attachments));
    }

    // ── 規則：大小 ───────────────────────────────────────

    [Fact]
    public void Constructor_WithExactlyMaxSize_Succeeds()
    {
        var @case = CreateCase([Jpg("大.jpg", AttachmentRules.MaxSize)]);

        Assert.Equal(AttachmentRules.MaxSize, @case.Attachments.Single().Size);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithEmptyOrNegativeSize_Throws(long size)
    {
        Assert.Throws<InvalidAttachmentException>(() => CreateCase([Jpg("空.jpg", size)]));
    }

    [Fact]
    public void Constructor_WithOneByteOverMaxSize_Throws()
    {
        Assert.Throws<InvalidAttachmentException>(() => CreateCase([Jpg("大.jpg", AttachmentRules.MaxSize + 1)]));
    }

    // 上限是 10 MB（業務規則），用常數寫死在測試裡：有人把 MaxSize 改掉，這個測試會提醒那是業務的決定
    [Fact]
    public void MaxSize_Is10MB()
    {
        Assert.Equal(10 * 1024 * 1024, AttachmentRules.MaxSize);
    }

    // ── 規則：類型與檔名 ─────────────────────────────────

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    public void Constructor_WithAllowedContentType_Succeeds(string contentType)
    {
        var @case = CreateCase([new NewAttachment("檔案", contentType, 1024)]);

        Assert.Equal(contentType, @case.Attachments.Single().ContentType);
    }

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")] // .docx
    [InlineData("application/x-msdownload")]                                                 // .exe
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]   // SVG 是文字檔，可以內嵌 <script>
    [InlineData("IMAGE/JPEG")]      // 不接受大小寫不同的寫法：ContentType 由 Application 決定，只會是固定的值
    [InlineData("")]
    public void Constructor_WithDisallowedContentType_Throws(string contentType)
    {
        Assert.Throws<InvalidAttachmentException>(() => CreateCase([new NewAttachment("檔案", contentType, 1024)]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankFileName_Throws(string fileName)
    {
        Assert.Throws<InvalidAttachmentException>(() => CreateCase([Jpg(fileName)]));
    }

    [Fact]
    public void Constructor_WithMaxFileNameLength_Succeeds()
    {
        var fileName = new string('a', AttachmentRules.MaxFileNameLength - 4) + ".jpg";

        var @case = CreateCase([Jpg(fileName)]);

        Assert.Equal(fileName, @case.Attachments.Single().FileName);
    }

    [Fact]
    public void Constructor_WithTooLongFileName_Throws()
    {
        var fileName = new string('a', AttachmentRules.MaxFileNameLength - 3) + ".jpg";

        Assert.Throws<InvalidAttachmentException>(() => CreateCase([Jpg(fileName)]));
    }

    // 只要有一個不合法，整批都不收（不是收下合法的那幾個）
    [Fact]
    public void Constructor_WithOneInvalidAmongValid_Throws()
    {
        Assert.Throws<InvalidAttachmentException>(
            () => CreateCase([Jpg("1.jpg"), new NewAttachment("2.exe", "application/x-msdownload", 1024), Jpg("3.jpg")]));
    }

    // ── 補件 ─────────────────────────────────────────────

    [Fact]
    public void SubmitSupplement_WithAttachments_LinksThemToThePendingRequestAndReturnsThem()
    {
        var @case = CreateCaseAwaitingSupplement([Jpg("送件.jpg")]);
        var pendingRequest = @case.SupplementRequests.Single();
        var submittedAt = Now.AddDays(1);

        var added = @case.SubmitSupplement("補上照片", submittedAt, [Png("補件1.png"), Pdf("補件2.pdf")]);

        Assert.Equal(["補件1.png", "補件2.pdf"], added.Select(a => a.FileName));
        Assert.All(added, a =>
        {
            Assert.Equal(pendingRequest.Id, a.SupplementRequestId);
            Assert.Equal(submittedAt, a.UploadedAt);
        });
        // 回傳的就是加進案件的同一批物件（Application 用它們的 Id 存檔），送件時的附件不受影響
        Assert.Equal(3, @case.Attachments.Count);
        Assert.All(added, a => Assert.Contains(a, @case.Attachments));
        Assert.Null(@case.Attachments.Single(a => a.FileName == "送件.jpg").SupplementRequestId);
    }

    [Fact]
    public void SubmitSupplement_Twice_EachAttachmentLinksToItsOwnRequest()
    {
        var @case = CreateCaseAwaitingSupplement([]);
        var first = @case.SubmitSupplement("第一次", Now.AddDays(1), [Jpg("第一次.jpg")]).Single();
        @case.RequestSupplement("還缺文件", Now.AddDays(2));
        var second = @case.SubmitSupplement("第二次", Now.AddDays(3), [Pdf("第二次.pdf")]).Single();

        var requests = @case.SupplementRequests.OrderBy(r => r.Sequence).ToList();
        Assert.Equal(requests[0].Id, first.SupplementRequestId);
        Assert.Equal(requests[1].Id, second.SupplementRequestId);
    }

    [Fact]
    public void SubmitSupplement_WithoutAttachments_ReturnsEmpty()
    {
        var @case = CreateCaseAwaitingSupplement([]);

        var added = @case.SubmitSupplement("文字說明就夠了", Now.AddDays(1), []);

        Assert.Empty(added);
        Assert.Equal(CaseStatus.UnderReview, @case.Status);
    }

    // 數量上限是「每次」：送件 5 個、補件再 5 個都可以
    [Fact]
    public void SubmitSupplement_MaxCountIsPerSubmission()
    {
        var max = AttachmentRules.MaxCountPerSubmission;
        var @case = CreateCaseAwaitingSupplement(Enumerable.Range(1, max).Select(i => Jpg($"送件{i}.jpg")).ToList());

        @case.SubmitSupplement("補件", Now.AddDays(1), Enumerable.Range(1, max).Select(i => Jpg($"補件{i}.jpg")).ToList());

        Assert.Equal(max * 2, @case.Attachments.Count);
    }

    // 全部檢查完才修改：附件不合法時，補件不能已經被標記成「已補件」，案件也還在補件中
    [Fact]
    public void SubmitSupplement_WithInvalidAttachment_ThrowsAndChangesNothing()
    {
        var @case = CreateCaseAwaitingSupplement([Jpg("送件.jpg")]);

        Assert.Throws<InvalidAttachmentException>(
            () => @case.SubmitSupplement("補件", Now.AddDays(1), [Jpg("太大.jpg", AttachmentRules.MaxSize + 1)]));

        Assert.Equal(CaseStatus.AwaitingSupplement, @case.Status);
        var request = @case.SupplementRequests.Single();
        Assert.Null(request.SubmittedAt);
        Assert.Null(request.SubmittedContent);
        Assert.Single(@case.Attachments);
    }

    // 狀態先檢查：不在補件中時，不管附件合不合法都是狀態錯誤（409），不是附件錯誤（400）
    [Fact]
    public void SubmitSupplement_WhenNotAwaitingSupplement_ThrowsStatusErrorEvenWithInvalidAttachment()
    {
        var @case = CreateCase([]);

        Assert.Throws<InvalidCaseStatusTransitionException>(
            () => @case.SubmitSupplement("補件", Now.AddDays(1), [Jpg("太大.jpg", AttachmentRules.MaxSize + 1)]));
        Assert.Empty(@case.Attachments);
    }

    // 內容是空白時（Domain 的另一種錯誤），附件也不能已經加進去
    [Fact]
    public void SubmitSupplement_WithBlankContent_DoesNotAddAttachments()
    {
        var @case = CreateCaseAwaitingSupplement([]);

        Assert.ThrowsAny<ArgumentException>(() => @case.SubmitSupplement(" ", Now.AddDays(1), [Jpg("補件.jpg")]));

        Assert.Empty(@case.Attachments);
        Assert.Equal(CaseStatus.AwaitingSupplement, @case.Status);
    }

    // ── Helpers ──────────────────────────────────────────

    private static NewAttachment Jpg(string fileName, long size = 1024) => new(fileName, "image/jpeg", size);
    private static NewAttachment Png(string fileName, long size = 1024) => new(fileName, "image/png", size);
    private static NewAttachment Pdf(string fileName, long size = 1024) => new(fileName, "application/pdf", size);

    private static Case CreateCase(IReadOnlyList<NewAttachment> attachments) =>
        new(CaseNumber.Create(new DateOnly(2026, 10, 27), 1), "路面坑洞", "中山路有坑洞", Petitioner, SubmittedBy, Now, attachments);

    private static Case CreateCaseAwaitingSupplement(IReadOnlyList<NewAttachment> attachments)
    {
        var @case = CreateCase(attachments);
        @case.AssignTo(Department.Id);
        @case.StartReview();
        @case.RequestSupplement("請補現場照片", Now);
        return @case;
    }
}
