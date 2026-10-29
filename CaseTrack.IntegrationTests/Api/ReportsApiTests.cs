using System.Net;
using CaseTrack.Application.Authorization;
using CaseTrack.Infrastructure.Persistence.Configurations;
using ClosedXML.Excel;
using static CaseTrack.IntegrationTests.Api.ApiTestHelpers;

namespace CaseTrack.IntegrationTests.Api;

// 報表匯出：下載的 Excel 用 ClosedXML 打開來檢查內容，不只看狀態碼。
// 資料庫是整個測試集合共用的，其他測試也會送件，所以統計比的是「送件前後的差」，清單找自己那一件
[Collection(nameof(DatabaseCollection))]
public class ReportsApiTests : IClassFixture<CaseTrackApiFactory>
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly CaseTrackApiFactory _factory;
    private readonly HttpClient _citizenClient;
    private readonly HttpClient _dispatcherClient;
    private readonly HttpClient _officerClient;

    public ReportsApiTests(CaseTrackApiFactory factory)
    {
        _factory = factory;
        _citizenClient = factory.CreateCitizenClient();
        _dispatcherClient = factory.CreateDispatcherClient();
        _officerClient = factory.CreateOfficerClient();
    }

    // 期間用台灣的日期，前後各多一天：測試剛好跑在午夜前後也不會落在期間外
    private static string Period
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
            return $"from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}";
        }
    }

    [Fact]
    public async Task Statistics_CountsNewCasesByDepartmentAndStatus()
    {
        var before = await ReadStatisticsAsync(_dispatcherClient);

        var assigned = await _citizenClient.SubmitCaseAsync("報表統計：已分派");
        await _dispatcherClient.AssignAsync(assigned.Id, TestTokens.OfficerDepartmentId);
        await _citizenClient.SubmitCaseAsync("報表統計：未分派");

        var after = await ReadStatisticsAsync(_dispatcherClient);
        Assert.Equal(before.Get("工務局", "已送件") + 1, after.Get("工務局", "已送件"));
        Assert.Equal(before.Get("未分派", "已送件") + 1, after.Get("未分派", "已送件"));
        Assert.Equal(before.Get("合計", "合計") + 2, after.Get("合計", "合計"));
    }

    // 每個狀態都有欄位（包含沒有案件的），報表的欄位每次都一樣
    [Fact]
    public async Task Statistics_HasColumnForEveryStatus()
    {
        var statistics = await ReadStatisticsAsync(_dispatcherClient);

        Assert.Equal(["承辦單位", "已送件", "審核中", "補件中", "待核定", "已結案", "已退件", "合計"], statistics.Header);
    }

    // 範圍跟列表一樣：別單位的承辦人匯出的統計裡沒有工務局
    [Fact]
    public async Task Statistics_OfficerOfOtherDepartment_SeesOnlyOwnDepartment()
    {
        var assigned = await _citizenClient.SubmitCaseAsync("報表範圍：工務局");
        await _dispatcherClient.AssignAsync(assigned.Id, TestTokens.OfficerDepartmentId);
        var transportOfficer = _factory.CreateClientFor(
            "u-officer-tra", [Roles.CaseOfficer], DepartmentSeed.Transportation);

        var statistics = await ReadStatisticsAsync(transportOfficer);

        Assert.DoesNotContain("工務局", statistics.Departments);
        Assert.DoesNotContain("未分派", statistics.Departments);
    }

    [Fact]
    public async Task CaseList_ContainsCaseWithoutPersonalData()
    {
        var submitted = await _citizenClient.SubmitCaseAsync("報表清單：路燈不亮");

        using var workbook = await DownloadAsync(_dispatcherClient, $"api/reports/case-list?{Period}");

        var sheet = workbook.Worksheet(1);
        var row = FindRow(sheet, submitted.CaseNumber);
        Assert.Equal("報表清單：路燈不亮", sheet.Cell(row, 2).GetString());
        Assert.Equal("已送件", sheet.Cell(row, 3).GetString());
        Assert.Equal("未分派", sheet.Cell(row, 4).GetString());
        // 個資最小化：陳情人的姓名、電話不能出現在檔案的任何地方（CaseForm 預設的陳情人）
        var allText = string.Join("\n", sheet.CellsUsed().Select(c => c.GetString()));
        Assert.DoesNotContain("王小明", allText);
        Assert.DoesNotContain("0912345678", allText);
    }

    // 公式注入：主旨是民眾輸入的，寫成公式的樣子也只能是文字
    [Fact]
    public async Task CaseList_SubjectLookingLikeFormula_IsStoredAsText()
    {
        const string subject = "=HYPERLINK(\"http://evil.example\",\"點我\")";
        var submitted = await _citizenClient.SubmitCaseAsync(subject);

        using var workbook = await DownloadAsync(_dispatcherClient, $"api/reports/case-list?{Period}");

        var cell = workbook.Worksheet(1).Cell(FindRow(workbook.Worksheet(1), submitted.CaseNumber), 2);
        Assert.False(cell.HasFormula);
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal(subject, cell.GetString());
    }

    // 下載的標頭：類型、中文檔名、一律下載、不快取
    [Fact]
    public async Task CaseList_Headers_AreExcelAttachmentWithoutCache()
    {
        var response = await _dispatcherClient.GetAsync(
            $"api/reports/case-list?from=2026-10-01&to=2026-10-31", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("案件清單_20261001-20261031.xlsx", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    // 管理者不能看案件內容，民眾不是承辦端：都是 403
    [Theory]
    [InlineData(Roles.Administrator)]
    [InlineData(Roles.Citizen)]
    public async Task Reports_OtherRoles_ReturnForbidden(string role)
    {
        var client = _factory.CreateClientFor("u-report-" + role, [role], DepartmentSeed.PublicWorks);

        var response = await client.GetAsync($"api/reports/statistics?{Period}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reports_FromAfterTo_ReturnsValidationProblem()
    {
        var response = await _officerClient.GetAsync(
            "api/reports/statistics?from=2026-10-31&to=2026-10-01", TestContext.Current.CancellationToken);

        await AssertValidationProblemAsync(response, "From", "開始日期不能晚於結束日期");
    }

    [Fact]
    public async Task Reports_WithoutDates_ReturnsValidationProblem()
    {
        var response = await _officerClient.GetAsync("api/reports/case-list", TestContext.Current.CancellationToken);

        await AssertValidationProblemAsync(response, "From", "請選擇開始日期");
    }

    [Fact]
    public async Task Reports_PeriodLongerThanLimit_ReturnsValidationProblem()
    {
        var response = await _officerClient.GetAsync(
            "api/reports/statistics?from=2025-01-01&to=2026-10-31", TestContext.Current.CancellationToken);

        await AssertValidationProblemAsync(response, "To");
    }

    // ── Helpers ──────────────────────────────────────────

    private static async Task<XLWorkbook> DownloadAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        return new XLWorkbook(stream);
    }

    // 表頭在第 5 列（前面是標題、期間、產製時間），資料從第 6 列開始
    private const int HeaderRow = 5;

    private static int FindRow(IXLWorksheet sheet, string caseNumber) =>
        sheet.Column(1).CellsUsed().Single(c => c.GetString() == caseNumber).Address.RowNumber;

    private static async Task<StatisticsTable> ReadStatisticsAsync(HttpClient client)
    {
        using var workbook = await DownloadAsync(client, $"api/reports/statistics?{Period}");
        var sheet = workbook.Worksheet(1);
        var header = sheet.Row(HeaderRow).CellsUsed().Select(c => c.GetString()).ToList();
        var values = new Dictionary<(string, string), int>();
        var departments = new List<string>();
        for (var row = HeaderRow + 1; !sheet.Cell(row, 1).IsEmpty(); row++)
        {
            var department = sheet.Cell(row, 1).GetString();
            departments.Add(department);
            for (var column = 2; column <= header.Count; column++)
            {
                values[(department, header[column - 1])] = sheet.Cell(row, column).GetValue<int>();
            }
        }
        return new StatisticsTable(header, departments, values);
    }

    private sealed record StatisticsTable(
        IReadOnlyList<string> Header, IReadOnlyList<string> Departments, Dictionary<(string, string), int> Values)
    {
        // 這個單位沒有出現在表上（期間內沒有案件）就是 0
        public int Get(string department, string column) => Values.GetValueOrDefault((department, column));
    }
}
