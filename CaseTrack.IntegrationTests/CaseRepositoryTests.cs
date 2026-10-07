using CaseTrack.Application.Abstractions;
using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CaseTrack.IntegrationTests;

// 透過 AddInfrastructure 的 DI 取得 Repository，測的是正式環境實際使用的組合（包括 DI 註冊方式）
[Collection(nameof(DatabaseCollection))]
public class CaseRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task GetByIdAsync_WithSupplementRequests_IncludesRequests()
    {
        // Arrange：在一個 scope（模擬一個請求）裡新增並存檔
        var cancellationToken = TestContext.Current.CancellationToken;
        var @case = new Case(NewCaseNumber(), "路燈不亮", "瑞光路301號附近");
        @case.StartReview();
        @case.RequestSupplement("缺照片", Now);

        await using (var writeScope = CreateScope())
        {
            writeScope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        }

        // Act：在另一個 scope（另一個請求）裡讀出
        await using var readScope = CreateScope();
        var reloaded = await readScope.ServiceProvider.GetRequiredService<ICaseRepository>()
            .GetByIdAsync(@case.Id, cancellationToken);

        // Assert
        Assert.NotNull(reloaded);
        Assert.Equal(CaseStatus.AwaitingSupplement, reloaded.Status);
        Assert.Equal("路燈不亮", reloaded.Subject);
        var request = Assert.Single(reloaded.SupplementRequests);
        Assert.Equal("缺照片", request.Reason);
        Assert.Equal(TimeSpan.FromHours(8), request.RequestedAt.Offset);
        Assert.Null(request.SubmittedAt);
    }

    // 每呼叫一次就模擬一個新的 HTTP 請求：裡面的 DbContext、Repository 都是全新的實例
    private static AsyncServiceScope CreateScope()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(DatabaseFixture.ConnectionString);
        return services.BuildServiceProvider().CreateAsyncScope();
    }

    private static string NewCaseNumber() => $"T-{Guid.NewGuid():N}"[..20];
}
