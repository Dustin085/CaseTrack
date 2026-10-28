using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CaseTrack.Api.Common;
using CaseTrack.Application.Cases;
using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CaseTrack.IntegrationTests.Api.ApiTestHelpers;

namespace CaseTrack.IntegrationTests.Api;

// 送件與補件附檔：multipart/form-data → 檔頭檢查 → 存檔 → 資料庫。檔案存在 TestData.FileStorageRoot（暫存資料夾）
[Collection(nameof(DatabaseCollection))]
public class AttachmentUploadApiTests : IClassFixture<CaseTrackApiFactory>
{
    private readonly CaseTrackApiFactory _factory;
    private readonly HttpClient _citizenClient;
    private readonly HttpClient _dispatcherClient;
    private readonly HttpClient _officerClient;

    public AttachmentUploadApiTests(CaseTrackApiFactory factory)
    {
        _factory = factory;
        _citizenClient = factory.CreateCitizenClient();
        _dispatcherClient = factory.CreateDispatcherClient();
        _officerClient = factory.CreateOfficerClient();
    }

    // ── 送件 ─────────────────────────────────────────────

    // 類型由檔頭決定（瀏覽器送的 application/octet-stream 不採用）；中文檔名完整保留；檔案內容原封不動存進去
    [Fact]
    public async Task SubmitCase_WithAttachments_SavesFilesAndRecords()
    {
        var response = await _citizenClient.PostCaseAsync(CaseForm(files:
        [
            new TestFile("現場照片.jpg", JpegBytes),
            new TestFile("位置圖.pdf", PdfBytes),
        ]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var submitted = await response.Content.ReadFromJsonAsync<SubmitCaseResult>(TestContext.Current.CancellationToken);
        Assert.NotNull(submitted);

        var attachments = await LoadAttachmentsAsync(submitted.Id);
        Assert.Collection(attachments.OrderBy(a => a.FileName),
            a =>
            {
                Assert.Equal("位置圖.pdf", a.FileName);
                Assert.Equal("application/pdf", a.ContentType);
                Assert.Equal(PdfBytes.Length, a.Size);
                Assert.Null(a.SupplementRequestId);
            },
            a =>
            {
                Assert.Equal("現場照片.jpg", a.FileName);
                Assert.Equal("image/jpeg", a.ContentType);
            });
        Assert.All(attachments, a => Assert.True(File.Exists(StoredPath(a.Id)), $"找不到 {a.FileName} 的檔案"));
        Assert.Equal(JpegBytes, await File.ReadAllBytesAsync(StoredPath(attachments.Single(a => a.FileName == "現場照片.jpg").Id),
            TestContext.Current.CancellationToken));
    }

    // 沒有附件也能送件（大部分的陳情只有文字）
    [Fact]
    public async Task SubmitCase_WithoutAttachments_Succeeds()
    {
        var response = await _citizenClient.PostCaseAsync(CaseForm());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── 不合法的檔案 ─────────────────────────────────────

    // 改名成 .jpg 的執行檔（MZ 開頭），即使瀏覽器說它是 image/jpeg
    [Fact]
    public async Task SubmitCase_ExecutableRenamedToJpg_ReturnsValidationProblem()
    {
        var response = await _citizenClient.PostCaseAsync(CaseForm(files:
            [new TestFile("photo.jpg", [0x4D, 0x5A, 0x90, 0x00, 0x03], "image/jpeg")]));

        await AssertValidationProblemAsync(response, "Attachments");
    }

    // 內容是 PNG，檔名是 .exe：下載後 Windows 會試著執行
    [Fact]
    public async Task SubmitCase_PngNamedExe_ReturnsValidationProblem()
    {
        var response = await _citizenClient.PostCaseAsync(CaseForm(files: [new TestFile("photo.exe", PngBytes)]));

        await AssertValidationProblemAsync(response, "Attachments");
    }

    [Fact]
    public async Task SubmitCase_TooManyFiles_ReturnsValidationProblem()
    {
        var files = Enumerable.Range(1, AttachmentRules.MaxCountPerSubmission + 1)
            .Select(i => new TestFile($"{i}.jpg", JpegBytes)).ToArray();

        var response = await _citizenClient.PostCaseAsync(CaseForm(files: files));

        await AssertValidationProblemAsync(response, "Attachments");
    }

    // 單檔超過 10 MB，但整個請求還在上限內：由 Domain 的規則擋下（400），不是 413
    [Fact]
    public async Task SubmitCase_FileOverMaxSize_ReturnsBadRequest()
    {
        var tooLarge = new byte[AttachmentRules.MaxSize + 1];
        JpegBytes.CopyTo(tooLarge, 0);

        var response = await _citizenClient.PostCaseAsync(CaseForm(files: [new TestFile("大.jpg", tooLarge)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    // ── 請求的格式與大小 ─────────────────────────────────

    // [Consumes] 只接受 multipart：送 JSON 是 415，不會綁定出一個空的物件再回「主旨不可為空」
    [Fact]
    public async Task SubmitCase_WithJsonBody_ReturnsUnsupportedMediaType()
    {
        var response = await _citizenClient.PostAsJsonAsync("api/my/cases",
            new { subject = "路燈不亮", content = "中山路路燈故障", petitioner = new { name = "王小明", phone = "0912345678" } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task SubmitSupplement_WithJsonBody_ReturnsUnsupportedMediaType()
    {
        var caseId = await CreateCaseAwaitingSupplementAsync();

        var response = await _citizenClient.PostAsJsonAsync($"api/my/cases/{caseId}/supplement-submission",
            new { content = "已補上照片" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    // ── 補件 ─────────────────────────────────────────────

    // 補件的附件連到那一次補件；送件時沒有附件，補件時才附上
    [Fact]
    public async Task SubmitSupplement_WithAttachment_LinksToSupplementRequest()
    {
        var caseId = await CreateCaseAwaitingSupplementAsync();

        var response = await _citizenClient.PostSupplementAsync(caseId,
            SupplementForm("已補上地圖", new TestFile("地圖.png", PngBytes)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var @case = await scope.ServiceProvider.GetRequiredService<CaseTrackDbContext>().Cases
            .Include(c => c.SupplementRequests)
            .Include(c => c.Attachments)
            .SingleAsync(c => c.Id == caseId, TestContext.Current.CancellationToken);
        var attachment = Assert.Single(@case.Attachments);
        Assert.Equal(@case.SupplementRequests.Single().Id, attachment.SupplementRequestId);
        Assert.Equal("image/png", attachment.ContentType);
        Assert.True(File.Exists(StoredPath(attachment.Id)));
    }

    // 補件的附件不合法：補件不成立（案件還在補件中），民眾修正後可以再送一次
    [Fact]
    public async Task SubmitSupplement_WithInvalidAttachment_KeepsAwaitingSupplement()
    {
        var caseId = await CreateCaseAwaitingSupplementAsync();

        var response = await _citizenClient.PostSupplementAsync(caseId,
            SupplementForm("已補上", new TestFile("report.docx", [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00])));

        await AssertValidationProblemAsync(response, "Attachments");
        var retry = await _citizenClient.PostSupplementAsync(caseId, SupplementForm("已補上", new TestFile("report.pdf", PdfBytes)));
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────

    private static string StoredPath(Guid attachmentId) => Path.Combine(TestData.FileStorageRoot, attachmentId.ToString("N"));

    private async Task<IReadOnlyList<Attachment>> LoadAttachmentsAsync(Guid caseId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var @case = await scope.ServiceProvider.GetRequiredService<CaseTrackDbContext>().Cases
            .Include(c => c.Attachments)
            .SingleAsync(c => c.Id == caseId, TestContext.Current.CancellationToken);
        return @case.Attachments.ToList();
    }

    private async Task<Guid> CreateCaseAwaitingSupplementAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var submitted = await _citizenClient.SubmitCaseAsync();
        await _dispatcherClient.AssignAsync(submitted.Id, TestTokens.OfficerDepartmentId);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _officerClient.PostAsync($"api/cases/{submitted.Id}/review", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _officerClient.PostAsJsonAsync($"api/cases/{submitted.Id}/supplement-requests",
                new { reason = "請補照片" }, cancellationToken)).StatusCode);
        return submitted.Id;
    }
}
