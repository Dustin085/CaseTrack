using System.Data.Common;
using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Cases;
using CaseTrack.Application.Common;
using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure;
using CaseTrack.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CaseTrack.IntegrationTests;

// 案件列表（ICaseRepository.ListAsync）。
// 資料庫是所有測試共用的，裡面有其他測試留下的案件：每個測試都在主旨加上自己的標記，再用關鍵字篩選，
// 只看自己建立的案件，總筆數才能精確比對
[Collection(nameof(DatabaseCollection))]
public class CaseListTests
{
    // ── 範圍 ─────────────────────────────────────────────

    // 總筆數也只能算看得到的案件：從整張表算的話，數字是錯的，還會洩漏全系統的案件數
    [Fact]
    public async Task ListAsync_DepartmentScope_ReturnsAndCountsOnlyThatDepartment()
    {
        var marker = NewMarker();
        await SaveCaseAsync(marker, DepartmentSeed.PublicWorks);
        await SaveCaseAsync(marker, DepartmentSeed.PublicWorks);
        await SaveCaseAsync(marker, DepartmentSeed.Transportation);
        await SaveCaseAsync(marker, departmentId: null);

        var result = await ListAsync(Query(marker), CaseAccessScope.ForDepartment(DepartmentSeed.PublicWorks));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, c => Assert.Equal(DepartmentSeed.PublicWorks, c.DepartmentId));
    }

    // 分派人員看得到全部，包括還沒分派的
    [Fact]
    public async Task ListAsync_AllScope_IncludesUnassigned()
    {
        var marker = NewMarker();
        await SaveCaseAsync(marker, DepartmentSeed.PublicWorks);
        await SaveCaseAsync(marker, departmentId: null);

        var result = await ListAsync(Query(marker), CaseAccessScope.All);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, c => c.DepartmentId is null);
    }

    [Fact]
    public async Task ListAsync_NoneScope_ReturnsEmpty()
    {
        var marker = NewMarker();
        await SaveCaseAsync(marker, DepartmentSeed.PublicWorks);

        var result = await ListAsync(Query(marker), CaseAccessScope.None);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    // 民眾只看得到自己送出的案件，總筆數也只算自己的。
    // 民眾 B 的案件「已分派」：如果誤用了其他範圍（例如單位），會把 B 的案件也算進來
    [Fact]
    public async Task ListAsync_PetitionerScope_ReturnsOnlyOwnCases()
    {
        var marker = NewMarker();
        var citizenA = $"u-a-{Guid.NewGuid():N}";
        var citizenB = $"u-b-{Guid.NewGuid():N}";
        await SaveCaseAsync(marker, departmentId: null, submittedBy: citizenA);
        await SaveCaseAsync(marker, DepartmentSeed.PublicWorks, submittedBy: citizenA);
        await SaveCaseAsync(marker, DepartmentSeed.PublicWorks, submittedBy: citizenB);

        var result = await ListAsync(Query(marker), CaseAccessScope.ForPetitioner(citizenA));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    // ── 篩選 ─────────────────────────────────────────────

    // 關鍵字只出現在主旨、沒出現在案件編號，也要找得到（主旨「或」案件編號，不是「而且」）
    [Fact]
    public async Task ListAsync_KeywordOnlyInSubject_FindsCase()
    {
        var marker = NewMarker();
        await SaveCaseAsync(marker, DepartmentSeed.PublicWorks);

        var result = await ListAsync(Query(marker), CaseAccessScope.All);

        Assert.Single(result.Items);
    }

    // 反方向：關鍵字只出現在案件編號
    [Fact]
    public async Task ListAsync_KeywordOnlyInCaseNumber_FindsCase()
    {
        var caseNumber = await SaveCaseAsync(NewMarker(), DepartmentSeed.PublicWorks);

        var result = await ListAsync(Query(caseNumber), CaseAccessScope.All);

        Assert.Equal(caseNumber, Assert.Single(result.Items).CaseNumber);
    }

    // % 和 _ 是 SQL LIKE 的萬用字元：沒有跳脫的話，「標記-%」會匹配到「標記-abc」
    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    public async Task ListAsync_KeywordWithLikeWildcard_IsMatchedLiterally(string wildcard)
    {
        var marker = NewMarker();
        await SaveCaseAsync($"{marker}-abc", DepartmentSeed.PublicWorks);

        var result = await ListAsync(Query($"{marker}-{wildcard}"), CaseAccessScope.All);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ListAsync_StatusFilter_ReturnsOnlyThatStatus()
    {
        var marker = NewMarker();
        await SaveCaseAsync(marker, DepartmentSeed.PublicWorks);
        await SaveCaseAsync(marker, DepartmentSeed.PublicWorks, startReview: true);

        var result = await ListAsync(Query(marker) with { Status = CaseStatus.UnderReview }, CaseAccessScope.All);

        Assert.Equal(CaseStatus.UnderReview, Assert.Single(result.Items).Status);
        Assert.Equal(1, result.TotalCount);
    }

    // 關鍵字只有空白：當成沒有篩選，不是去搜尋空白。總筆數要跟完全沒有關鍵字時一樣
    [Fact]
    public async Task ListAsync_WhitespaceKeyword_IsIgnored()
    {
        await SaveCaseAsync(NewMarker(), DepartmentSeed.PublicWorks);

        var withoutKeyword = await ListAsync(new CaseListQuery(null, null, 1, 1), CaseAccessScope.All);
        var withWhitespace = await ListAsync(new CaseListQuery(null, "   ", 1, 1), CaseAccessScope.All);

        Assert.Equal(withoutKeyword.TotalCount, withWhitespace.TotalCount);
    }

    // ── 分頁 ─────────────────────────────────────────────

    // 5 筆案件的送件時間完全相同（TestData.SubmittedAt），只能靠 CaseNumber 決定順序：
    // 排序不穩定的話，翻頁時會重複或漏掉
    [Fact]
    public async Task ListAsync_Paging_ReturnsEachCaseExactlyOnceInStableOrder()
    {
        var marker = NewMarker();
        var caseNumbers = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            caseNumbers.Add(await SaveCaseAsync(marker, DepartmentSeed.PublicWorks));
        }

        var page1 = await ListAsync(Query(marker, page: 1, pageSize: 2), CaseAccessScope.All);
        var page2 = await ListAsync(Query(marker, page: 2, pageSize: 2), CaseAccessScope.All);
        var page3 = await ListAsync(Query(marker, page: 3, pageSize: 2), CaseAccessScope.All);

        Assert.Equal([2, 2, 1], [page1.Items.Count, page2.Items.Count, page3.Items.Count]);
        Assert.All([page1, page2, page3], p => Assert.Equal(5, p.TotalCount));
        var listed = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(c => c.CaseNumber).ToList();
        Assert.Equal(caseNumbers.OrderByDescending(n => n, StringComparer.Ordinal), listed);
    }

    // ── 不讀出個資 ───────────────────────────────────────

    // 列表的 SQL 只能選摘要需要的欄位。Select 裡如果呼叫自訂方法，EF 會把整個 Case（含陳情人）讀出來，
    // 回傳的資料看起來一樣，只有看 SQL 才發現得了
    [Fact]
    public async Task ListAsync_GeneratedSql_DoesNotSelectPetitionerColumns()
    {
        var marker = NewMarker();
        await SaveCaseAsync(marker, DepartmentSeed.PublicWorks);
        var recorder = new SqlRecorder();

        await using var scope = CreateScope(recorder);
        var result = await scope.ServiceProvider.GetRequiredService<ICaseRepository>()
            .ListAsync(Query(marker), CaseAccessScope.All, TestContext.Current.CancellationToken);

        Assert.Single(result.Items);
        var listSql = Assert.Single(recorder.Commands, sql => sql.Contains("OFFSET"));
        Assert.DoesNotContain("Petitioner_", listSql);
        Assert.DoesNotContain("[Content]", listSql);
    }

    // ── Helpers ──────────────────────────────────────────

    private static string NewMarker() => $"列表測試-{Guid.NewGuid():N}"[..20];

    private static CaseListQuery Query(string keyword, int page = 1, int pageSize = CaseListQuery.DefaultPageSize) =>
        new(null, keyword, page, pageSize);

    // 存一筆案件，回傳案件編號
    private static async Task<string> SaveCaseAsync(
        string subject, Guid? departmentId, bool startReview = false, string? submittedBy = null)
    {
        var @case = new Case($"T-{Guid.NewGuid():N}"[..20], subject, "列表測試的內容",
            TestData.Petitioner, submittedBy ?? TestData.SubmittedBy, TestData.SubmittedAt, []);
        if (departmentId is { } id)
        {
            @case.AssignTo(id);
        }
        if (startReview)
        {
            @case.StartReview();
        }

        await using var scope = CreateScope();
        scope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return @case.CaseNumber;
    }

    private static async Task<PagedResult<CaseSummary>> ListAsync(CaseListQuery query, CaseAccessScope scope)
    {
        await using var serviceScope = CreateScope();
        return await serviceScope.ServiceProvider.GetRequiredService<ICaseRepository>()
            .ListAsync(query, scope, TestContext.Current.CancellationToken);
    }

    private static AsyncServiceScope CreateScope(SqlRecorder? recorder = null)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(DatabaseFixture.ConnectionString, TestData.FileStorageRoot);
        services.AddScoped<ICurrentUser>(_ => new FixedCurrentUser("test-user"));
        if (recorder is not null)
        {
            // EF Core 9 之後：在 AddDbContext 之外再補充設定，這裡多掛一個攔截器記錄 SQL
            services.ConfigureDbContext<Infrastructure.Persistence.CaseTrackDbContext>(options => options.AddInterceptors(recorder));
        }
        return services.BuildServiceProvider().CreateAsyncScope();
    }

    // 記錄實際送到資料庫的 SQL
    private sealed class SqlRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
