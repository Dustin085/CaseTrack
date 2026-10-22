using System.Text.Json;
using CaseTrack.Infrastructure.Persistence.Configurations;

namespace CaseTrack.IntegrationTests.Api;

// GET /api/cases/{id} 的 allowedActions：前端靠它決定顯示哪些按鈕。
// 直接看原始的 JSON：合約是「動作的名稱」，不依賴測試端的反序列化設定
[Collection(nameof(DatabaseCollection))]
public class AllowedActionsApiTests : IClassFixture<CaseTrackApiFactory>
{
    private readonly HttpClient _citizenClient;
    private readonly HttpClient _dispatcherClient;
    private readonly HttpClient _officerClient;

    public AllowedActionsApiTests(CaseTrackApiFactory factory)
    {
        _citizenClient = factory.CreateCitizenClient();
        _dispatcherClient = factory.CreateDispatcherClient();
        _officerClient = factory.CreateOfficerClient();
    }

    [Fact]
    public async Task GetCase_UnassignedByDispatcher_AllowsAssignAndDismiss()
    {
        var submitted = await _citizenClient.SubmitCaseAsync();

        var actions = await GetAllowedActionsAsync(_dispatcherClient, submitted.Id);

        Assert.Equal(["Assign", "Dismiss"], actions);
    }

    // 同一件案件，分派後換承辦人看：動作跟著角色和狀態改變
    [Fact]
    public async Task GetCase_AssignedByOfficer_AllowsReviewRejectAndViewPetitioner()
    {
        var submitted = await _citizenClient.SubmitCaseAsync();
        await _dispatcherClient.AssignAsync(submitted.Id, DepartmentSeed.PublicWorks);

        var actions = await GetAllowedActionsAsync(_officerClient, submitted.Id);

        Assert.Equal(["Review", "Reject", "ViewPetitioner"], actions);
    }

    private static async Task<string[]> GetAllowedActionsAsync(HttpClient client, Guid caseId)
    {
        var body = await client.GetStringAsync($"api/cases/{caseId}", TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("allowedActions").EnumerateArray()
            .Select(a => a.GetString()!)
            .ToArray();
    }
}
