using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure;
using CaseTrack.Infrastructure.Persistence;
using CaseTrack.Infrastructure.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace CaseTrack.IntegrationTests;

[Collection(nameof(DatabaseCollection))]
public class AuditInterceptorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task SaveChanges_NewCase_AddsAuditEntryWithCurrentUser()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var @case = new Case(NewCaseNumber(), "路燈不亮", "中山路路燈故障", TestData.Petitioner, TestData.SubmittedBy, TestData.SubmittedAt, []);

        // Act
        await AddCaseAsync(@case, cancellationToken);

        // Assert
        var auditEntries = await GetAuditEntriesAsync(@case.Id, cancellationToken);

        var auditEntry = Assert.Single(auditEntries);
        Assert.Equal("test-user", auditEntry.ChangedBy);
        Assert.Equal(Now, auditEntry.ChangedAt);
        Assert.Equal(TimeSpan.Zero, auditEntry.ChangedAt.Offset);
        Assert.Equal("Added", auditEntry.Action);
        Assert.Contains("路燈不亮", auditEntry.Changes);
    }

    // 值物件（Complex Type）的欄位不在 entry.Properties 裡，攔截器要另外展開才記錄得到；
    // 陳情人的欄位都是個資，只能記錄遮罩後的值
    [Fact]
    public async Task SaveChanges_NewCase_RecordsMaskedPetitionerFieldsWithPrefix()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var @case = new Case(NewCaseNumber(), "路燈不亮", "中山路路燈故障",
            new Petitioner("王小明", "0912345678", null, "臺北市中山區"),
            TestData.SubmittedBy, TestData.SubmittedAt, []);

        // Act
        await AddCaseAsync(@case, cancellationToken);

        // Assert
        var auditEntry = Assert.Single(await GetAuditEntriesAsync(@case.Id, cancellationToken));
        using var changes = JsonDocument.Parse(auditEntry.Changes);
        var root = changes.RootElement;
        Assert.Equal("王*明", root.GetProperty("Petitioner.Name").GetProperty("New").GetString());
        Assert.Equal("09******78", root.GetProperty("Petitioner.Phone").GetProperty("New").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Petitioner.Email").GetProperty("New").ValueKind);
        Assert.Equal("臺****區", root.GetProperty("Petitioner.Address").GetProperty("New").GetString());

        // 不管 JSON 的結構怎麼變，完整的個資都不能出現在稽核紀錄的任何地方
        Assert.DoesNotContain("0912345678", auditEntry.Changes);
        Assert.DoesNotContain("王小明", auditEntry.Changes);

        // 不是個資的欄位照常記錄
        Assert.Equal("路燈不亮", root.GetProperty(nameof(Case.Subject)).GetProperty("New").GetString());
    }

    // 同步版的 SaveChanges 走的是攔截器的另一個覆寫點（SavingChanges），要分開驗證
    [Fact]
    public async Task SaveChanges_Synchronous_AddsAuditEntry()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var @case = new Case(NewCaseNumber(), "路燈不亮", "中山路路燈故障", TestData.Petitioner, TestData.SubmittedBy, TestData.SubmittedAt, []);

        // Act：直接呼叫 DbContext 的同步 SaveChanges（IUnitOfWork 只有非同步版本）
        await using (var writeScope = CreateScope(Now))
        {
            var dbContext = writeScope.ServiceProvider.GetRequiredService<CaseTrackDbContext>();
            dbContext.Cases.Add(@case);
            dbContext.SaveChanges();
        }

        // Assert
        var auditEntries = await GetAuditEntriesAsync(@case.Id, cancellationToken);
        var auditEntry = Assert.Single(auditEntries);
        Assert.Equal("Added", auditEntry.Action);
    }

    [Fact]
    public async Task SaveChanges_StartReview_RecordsOnlyStatusChange()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var @case = new Case(NewCaseNumber(), "路燈不亮", "中山路路燈故障", TestData.Petitioner, TestData.SubmittedBy, TestData.SubmittedAt, []);
        @case.AssignTo(TestData.DepartmentId);

        // Act
        await AddCaseAsync(@case, cancellationToken);

        await using (var reloadScope = CreateScope(Now))
        {
            var reload = await reloadScope.ServiceProvider
                .GetRequiredService<CaseTrackDbContext>().Cases
                .SingleAsync(c => c.Id == @case.Id, cancellationToken);
            reload.StartReview();
            await reloadScope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(cancellationToken);
        }

        // Assert
        var auditEntries = await GetAuditEntriesAsync(@case.Id, cancellationToken);
        Assert.Collection(auditEntries,
            added => Assert.Equal("Added", added.Action),
            modified =>
            {
                AssertStatusChanged(modified, "Submitted", "UnderReview");
                Assert.Equal([nameof(Case.Status)], GetChangedPropertyNames(modified));
            });
    }

    // 證明稽核紀錄跟業務資料在同一個交易裡：業務資料沒寫進去，稽核紀錄也不會寫進去
    [Fact]
    public async Task SaveChanges_ConcurrencyConflict_DoesNotAddAuditEntry()
    {
        // Arrange：兩個 scope（兩個請求）讀出同一個案件，第一個先存檔成功
        var cancellationToken = TestContext.Current.CancellationToken;
        var @case = new Case(NewCaseNumber(), "路燈不亮", "中山路路燈故障", TestData.Petitioner, TestData.SubmittedBy, TestData.SubmittedAt, []);
        @case.AssignTo(TestData.DepartmentId);
        @case.StartReview();
        await AddCaseAsync(@case, cancellationToken);

        await using var firstScope = CreateScope(Now);
        var firstLoad = await firstScope.ServiceProvider.GetRequiredService<CaseTrackDbContext>()
            .Cases.SingleAsync(c => c.Id == @case.Id, cancellationToken);

        await using var secondScope = CreateScope(Now);
        var secondLoad = await secondScope.ServiceProvider.GetRequiredService<CaseTrackDbContext>()
            .Cases.SingleAsync(c => c.Id == @case.Id, cancellationToken);

        firstLoad.Reject("不符合規定");
        await firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .SaveChangesAsync(cancellationToken);

        // Act：第二個請求拿著舊的 RowVersion 存檔
        secondLoad.Close("已派員修復");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => secondScope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(cancellationToken));

        // Assert：只有新增和退件兩筆，失敗的結案沒有留下任何稽核紀錄
        var auditEntries = await GetAuditEntriesAsync(@case.Id, cancellationToken);
        Assert.Collection(auditEntries,
            added => Assert.Equal("Added", added.Action),
            rejected => AssertStatusChanged(rejected, "UnderReview", "Rejected"));
    }

    // Modified 的稽核紀錄裡，Status 的前後值符合預期（不檢查其他欄位：例如退件還會改 RejectionReason）
    private static void AssertStatusChanged(AuditEntry entry, string oldStatus, string newStatus)
    {
        Assert.Equal("Modified", entry.Action);
        using var changes = JsonDocument.Parse(entry.Changes);
        var status = changes.RootElement.GetProperty(nameof(Case.Status));
        Assert.Equal(oldStatus, status.GetProperty("Old").GetString());
        Assert.Equal(newStatus, status.GetProperty("New").GetString());
    }

    private static string[] GetChangedPropertyNames(AuditEntry entry)
    {
        using var changes = JsonDocument.Parse(entry.Changes);
        return changes.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
    }

    private static async Task AddCaseAsync(Case @case, CancellationToken ct)
    {
        await using (var writeScope = CreateScope(Now))
        {
            writeScope.ServiceProvider.GetRequiredService<CaseTrackDbContext>().Cases.Add(@case);
            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(ct);
        }
    }

    private static async Task<List<AuditEntry>> GetAuditEntriesAsync(Guid caseId, CancellationToken ct)
    {
        await using var readScope = CreateScope(Now);
        return await readScope.ServiceProvider
            .GetRequiredService<CaseTrackDbContext>().AuditEntries
            .Where(a => a.EntityType == nameof(Case) && a.EntityId == caseId.ToString())
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
    }

    private static AsyncServiceScope CreateScope(DateTimeOffset now)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(DatabaseFixture.ConnectionString);
        services.AddScoped<ICurrentUser>(_ => new FixedCurrentUser("test-user"));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        return services.BuildServiceProvider().CreateAsyncScope();
    }

    private static string NewCaseNumber() => $"T-{Guid.NewGuid():N}"[..20];
}
