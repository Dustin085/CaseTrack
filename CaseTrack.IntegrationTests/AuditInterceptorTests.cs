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
        var @case = new Case(NewCaseNumber(), "路燈不亮", "中山路路燈故障");

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

    // 同步版的 SaveChanges 走的是攔截器的另一個覆寫點（SavingChanges），要分開驗證
    [Fact]
    public async Task SaveChanges_Synchronous_AddsAuditEntry()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var @case = new Case(NewCaseNumber(), "路燈不亮", "中山路路燈故障");

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
        var @case = new Case(NewCaseNumber(), "路燈不亮", "中山路路燈故障");

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
        var @case = new Case(NewCaseNumber(), "路燈不亮", "中山路路燈故障");
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
        secondLoad.Close();
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
