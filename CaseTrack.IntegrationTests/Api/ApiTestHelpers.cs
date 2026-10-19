using System.Net;
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

    public static readonly object ValidPetitioner = new { name = "王小明", phone = "0912345678" };

    // 民眾送件。準備資料失敗時直接在這裡失敗，錯誤訊息才看得出是哪一步出問題
    public static async Task<SubmitCaseResult> SubmitCaseAsync(
        this HttpClient citizenClient, string subject = "路燈不亮", string content = "中山路路燈故障")
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = await citizenClient.PostAsJsonAsync("api/my/cases",
            new { subject, content, petitioner = ValidPetitioner }, cancellationToken);

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
