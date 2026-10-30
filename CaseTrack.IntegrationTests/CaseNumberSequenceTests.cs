using CaseTrack.Application.Abstractions;
using CaseTrack.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CaseTrack.IntegrationTests;

// 流水號的資料庫實作。編號的格式、用台灣的哪一天，是 Domain 的 CaseNumber（單元測試 CaseNumberTests）。
// 所有測試共用同一個資料庫，所以每個測試用不同的日期，計數器才不會互相影響
[Collection(nameof(DatabaseCollection))]
public class CaseNumberSequenceTests
{
    [Fact]
    public async Task NextAsync_FirstTwoCallsOfDay_ReturnsSequentialNumbersFromOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var date = new DateOnly(2099, 1, 1);

        var first = await NextAsync(provider, date, cancellationToken);
        var second = await NextAsync(provider, date, cancellationToken);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public async Task NextAsync_OnNextDay_RestartsFromOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        await NextAsync(provider, new DateOnly(2099, 2, 1), cancellationToken);
        await NextAsync(provider, new DateOnly(2099, 2, 1), cancellationToken);

        var number = await NextAsync(provider, new DateOnly(2099, 2, 2), cancellationToken);

        Assert.Equal(1, number);
    }

    // 50 個請求同時取號（而且都是當天第一次，會同時搶著建立計數器），不能有任何重複
    [Fact]
    public async Task NextAsync_ConcurrentRequests_ReturnsUniqueSequentialNumbers()
    {
        const int requestCount = 50;
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var date = new DateOnly(2099, 4, 1);

        // 每個工作用自己的 scope，模擬獨立的 HTTP 請求；DbContext 不是執行緒安全的，不能共用
        var numbers = await Task.WhenAll(
            Enumerable.Range(0, requestCount)
                .Select(_ => Task.Run(() => NextAsync(provider, date, cancellationToken), cancellationToken)));

        Assert.Equal(Enumerable.Range(1, requestCount), numbers.Order());
    }

    // 用正式的 AddInfrastructure 註冊
    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(DatabaseFixture.ConnectionString, TestData.FileStorageRoot);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICurrentUser>(_ => new FixedCurrentUser("test-user"));
        return services.BuildServiceProvider();
    }

    // 每次呼叫都開一個新的 scope，相當於一個新的請求
    private static async Task<int> NextAsync(ServiceProvider provider, DateOnly date, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var sequence = scope.ServiceProvider.GetRequiredService<ICaseNumberSequence>();
        return await sequence.NextAsync(date, cancellationToken);
    }
}
