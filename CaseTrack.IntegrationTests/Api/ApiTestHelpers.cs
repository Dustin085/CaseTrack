using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CaseTrack.Application.Cases;

namespace CaseTrack.IntegrationTests.Api;

// 多個 API 測試類別共用的操作與斷言
internal static class ApiTestHelpers
{
    public const string ProblemJson = "application/problem+json";

    // 讀 API 回應用的 JSON 設定：enum 只接受名稱（allowIntegerValues: false）。
    // 如果 API 退回成輸出數字，反序列化會直接失敗，不會悄悄通過
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    // ── 上傳用的表單 ─────────────────────────────────────

    // 測試用的檔案內容：只有檔頭是真的，後面隨便放幾個 bytes
    public static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    public static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
    public static readonly byte[] PdfBytes = "%PDF-1.7 test"u8.ToArray();

    // ContentType 是瀏覽器依副檔名猜的值（伺服器不採用），預設給一個不相干的值，證明類型是看檔頭決定的
    public sealed record TestFile(string FileName, byte[] Content, string ContentType = "application/octet-stream");

    // 送件的表單：欄位名稱和瀏覽器的 FormData 一樣（巢狀物件用 Petitioner.Name），檔案都放在 Attachments
    public static MultipartFormDataContent CaseForm(
        string subject = "路燈不亮",
        string content = "中山路路燈故障",
        string petitionerName = "王小明",
        string? phone = "0912345678",
        string? email = null,
        params TestFile[] files)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(subject), "Subject" },
            { new StringContent(content), "Content" },
            { new StringContent(petitionerName), "Petitioner.Name" },
        };
        if (phone is not null) form.Add(new StringContent(phone), "Petitioner.Phone");
        if (email is not null) form.Add(new StringContent(email), "Petitioner.Email");
        AddFiles(form, files);
        return form;
    }

    public static MultipartFormDataContent SupplementForm(string content = "已補上現場照片的說明", params TestFile[] files)
    {
        var form = new MultipartFormDataContent { { new StringContent(content), "Content" } };
        AddFiles(form, files);
        return form;
    }

    // 同一個欄位名稱（Attachments）加好幾次，就是多個檔案。
    // 檔名有中文時，HttpClient 會同時送出 filename 和 filename*（RFC 5987 的 UTF-8 寫法）
    private static void AddFiles(MultipartFormDataContent form, TestFile[] files)
    {
        foreach (var file in files)
        {
            var part = new ByteArrayContent(file.Content);
            part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            form.Add(part, "Attachments", file.FileName);
        }
    }

    public static Task<HttpResponseMessage> PostCaseAsync(this HttpClient client, MultipartFormDataContent form) =>
        client.PostAsync("api/my/cases", form, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> PostSupplementAsync(this HttpClient client, Guid caseId, MultipartFormDataContent form) =>
        client.PostAsync($"api/my/cases/{caseId}/supplement-submission", form, TestContext.Current.CancellationToken);

    // 民眾送件。準備資料失敗時直接在這裡失敗，錯誤訊息才看得出是哪一步出問題
    public static async Task<SubmitCaseResult> SubmitCaseAsync(
        this HttpClient citizenClient, string subject = "路燈不亮", string content = "中山路路燈故障")
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = await citizenClient.PostCaseAsync(CaseForm(subject, content));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SubmitCaseResult>(cancellationToken);
        Assert.NotNull(result);
        return result;
    }

    public static Task<HttpResponseMessage> PostAssignmentAsync(this HttpClient client, Guid caseId, Guid departmentId) =>
        client.PostAsJsonAsync($"api/cases/{caseId}/assignment", new { departmentId },
            TestContext.Current.CancellationToken);

    // 分派人員分派。受理之前一定要先分派
    public static async Task AssignAsync(this HttpClient dispatcherClient, Guid caseId, Guid departmentId)
    {
        var response = await dispatcherClient.PostAssignmentAsync(caseId, departmentId);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // 沒有回應內容的動作（受理、核定、停用單位⋯）成功 → 204
    public static async Task AssertNoContentAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // 驗證失敗 → 400 ProblemDetails，而且 errors 裡指出是哪個欄位。
    // 同一個欄位可能因為不同原因失敗（例如沒填、不存在），要確定是哪一個原因時，傳入 expectedMessage
    public static async Task AssertValidationProblemAsync(
        HttpResponseMessage response, string field, string? expectedMessage = null)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out var messages),
            $"errors 裡沒有 {field}：{problem.RootElement}");
        if (expectedMessage is not null)
        {
            Assert.Contains(expectedMessage, messages.EnumerateArray().Select(m => m.GetString()));
        }
    }
}
