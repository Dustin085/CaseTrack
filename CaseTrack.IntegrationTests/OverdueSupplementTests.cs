using CaseTrack.Api.BackgroundJobs;
using CaseTrack.Application;
using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Cases;
using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CaseTrack.IntegrationTests;

// 逾期查詢是手寫的資料庫條件，跟 Domain 的 IsSupplementOverdue 規則重複，
// 這裡驗證兩邊一致：資料庫查出來的，就是 Domain 認為逾期的。
// 所有測試共用同一個資料庫，所以只檢查特定的 Id 有沒有被查出來，不檢查數量。
[Collection(nameof(DatabaseCollection))]
public class OverdueSupplementTests
{
    private static readonly TimeSpan Taiwan = TimeSpan.FromHours(8);
    private static readonly DateTimeOffset Now = new(2099, 1, 1, 10, 0, 0, Taiwan);

    [Fact]
    public async Task GetOverdueSupplementCaseIdsAsync_WithOverdueCase_ReturnsCaseId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // 一個 provider 等於一個應用程式；之後每個 scope 等於一個請求
        await using var provider = CreateProvider(Now);

        // Arrange：在一個 scope 裡建立「15 天前要求補件」的案件
        var @case = new Case(NewCaseNumber(), "Test Subject.", "Test Content.");
        @case.StartReview();
        @case.RequestSupplement("Test Request.", Now - Case.SupplementDeadline - TimeSpan.FromDays(1));
        await using (var writeScope = provider.CreateAsyncScope())
        {
            writeScope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        }

        // Act：在另一個 scope 裡查詢
        await using var readScope = provider.CreateAsyncScope();
        var ids = await readScope.ServiceProvider.GetRequiredService<ICaseRepository>()
            .GetOverdueSupplementCaseIdsAsync(Now, 100, cancellationToken);

        // Assert
        Assert.Contains(@case.Id, ids);
    }

    [Fact]
    public async Task GetOverdueSupplementCaseIdsAsync_WithNonOverdueCase_ReturnsNotContainCaseId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // 一個 provider 等於一個應用程式；之後每個 scope 等於一個請求
        await using var provider = CreateProvider(Now);

        // Arrange：在一個 scope 裡建立「13 天前要求補件」的案件
        var @case = new Case(NewCaseNumber(), "Test Subject.", "Test Content.");
        @case.StartReview();
        @case.RequestSupplement("Test Request.", Now - Case.SupplementDeadline + TimeSpan.FromDays(1));
        await using (var writeScope = provider.CreateAsyncScope())
        {
            writeScope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        }

        // Act：在另一個 scope 裡查詢
        await using var readScope = provider.CreateAsyncScope();
        var ids = await readScope.ServiceProvider.GetRequiredService<ICaseRepository>()
            .GetOverdueSupplementCaseIdsAsync(Now, 100, cancellationToken);

        // Assert
        Assert.DoesNotContain(@case.Id, ids);
    }

    [Fact]
    public async Task HandleAsync_WithOverdueCase_RejectsCase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(Now);

        // Arrange：在一個 scope 裡建立「15 天前要求補件」的案件
        var @case = new Case(NewCaseNumber(), "Test Subject.", "Test Content.");
        @case.StartReview();
        @case.RequestSupplement("Test Request.", Now - Case.SupplementDeadline - TimeSpan.FromDays(1));
        await using (var writeScope = provider.CreateAsyncScope())
        {
            writeScope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        }

        // Act：在另一個 scope 裡處理「這個測試自己建立的」案件。
        // 不要從查詢結果取 First()：共用資料庫裡還有其他測試建立的逾期案件，取到哪一個不固定。
        await using (var actScope = provider.CreateAsyncScope())
        {
            await actScope.ServiceProvider.GetRequiredService<RejectOverdueCaseHandler>()
                .HandleAsync(@case.Id, cancellationToken);
        }

        // Assert
        await using var readScope = provider.CreateAsyncScope();
        var reload = await readScope.ServiceProvider.GetRequiredService<ICaseRepository>()
            .GetByIdAsync(@case.Id, cancellationToken);
        Assert.NotNull(reload);
        Assert.Equal(CaseStatus.Rejected, reload.Status);
        Assert.Equal("逾期未補件", reload.RejectionReason);
    }

    // 查詢和處理之間有時間差：民眾可能剛好補件了。這時 Handler 應該靜靜地略過，不能丟例外，也不能退件。
    [Fact]
    public async Task HandleAsync_WhenSupplementAlreadySubmitted_LeavesCaseUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(Now);

        // Arrange：15 天前要求補件，但民眾昨天已經補件了（回到審核中）
        var @case = new Case(NewCaseNumber(), "Test Subject.", "Test Content.");
        @case.StartReview();
        @case.RequestSupplement("Test Request.", Now - Case.SupplementDeadline - TimeSpan.FromDays(1));
        @case.SubmitSupplement(Now - TimeSpan.FromDays(1));
        await using (var writeScope = provider.CreateAsyncScope())
        {
            writeScope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        }

        // Act：直接處理這個案件（模擬它在補件之前被查詢出來）
        await using (var actScope = provider.CreateAsyncScope())
        {
            await actScope.ServiceProvider.GetRequiredService<RejectOverdueCaseHandler>()
                .HandleAsync(@case.Id, cancellationToken);
        }

        // Assert：沒有被退件，補件紀錄也還在
        await using var readScope = provider.CreateAsyncScope();
        var reloaded = await readScope.ServiceProvider.GetRequiredService<ICaseRepository>()
            .GetByIdAsync(@case.Id, cancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal(CaseStatus.UnderReview, reloaded.Status);
        Assert.Null(reloaded.RejectionReason);
        Assert.NotNull(Assert.Single(reloaded.SupplementRequests).SubmittedAt);
    }

    [Fact]
    public async Task ScanOnceAsync_WithOverdueAndNonOverdueCases_RejectsOnlyOverdueCase()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(Now);

        var overdueCase = new Case(NewCaseNumber(), "Overdue Case", "Overdue case content");
        overdueCase.StartReview();
        overdueCase.RequestSupplement("Need more info", Now - Case.SupplementDeadline - TimeSpan.FromDays(1));

        var notOverdueCase = new Case(NewCaseNumber(), "Nonoverdue Case", "Nonoverdue case content");
        notOverdueCase.StartReview();
        notOverdueCase.RequestSupplement("Need more info", Now - Case.SupplementDeadline + TimeSpan.FromDays(1));

        await using (var writeScope = provider.CreateAsyncScope())
        {
            var repo = writeScope.ServiceProvider.GetRequiredService<ICaseRepository>();
            repo.Add(overdueCase);
            repo.Add(notOverdueCase);
            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(cancellationToken);
        }

        // Act
        var scanner = new OverdueSupplementScanner(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OverdueScanOptions { BatchSize = 100 }),
            provider.GetRequiredService<TimeProvider>(),
            NullLogger<OverdueSupplementScanner>.Instance);
        await scanner.ScanOnceAsync(cancellationToken);

        // Assert
        await using var readScope = provider.CreateAsyncScope();
        var readRepo = readScope.ServiceProvider.GetRequiredService<ICaseRepository>();
        var overdueReloaded = await readRepo.GetByIdAsync(overdueCase.Id, cancellationToken);
        Assert.NotNull(overdueReloaded);
        var nonoverdueReloaded = await readRepo.GetByIdAsync(notOverdueCase.Id, cancellationToken);
        Assert.NotNull(nonoverdueReloaded);

        Assert.Equal(CaseStatus.Rejected, overdueReloaded.Status);
        Assert.Equal(CaseStatus.AwaitingSupplement, nonoverdueReloaded.Status);
    }

    // 用正式的 AddInfrastructure／AddApplication 註冊，只把時間來源換成固定的時間
    private static ServiceProvider CreateProvider(DateTimeOffset now)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(DatabaseFixture.ConnectionString);
        services.AddApplication();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        return services.BuildServiceProvider();
    }

    private static string NewCaseNumber() => $"T-{Guid.NewGuid():N}"[..20];
}
