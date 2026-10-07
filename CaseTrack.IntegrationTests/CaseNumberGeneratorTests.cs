using CaseTrack.Application.Abstractions;
using CaseTrack.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CaseTrack.IntegrationTests;

// 所有測試共用同一個資料庫，所以每個測試用不同的日期，計數器才不會互相影響
[Collection(nameof(DatabaseCollection))]
public class CaseNumberGeneratorTests
{
    private static readonly TimeSpan Taiwan = TimeSpan.FromHours(8);

    [Fact]
    public async Task NextAsync_FirstTwoCallsOfDay_ReturnsSequentialNumbersFromOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(new DateTimeOffset(2099, 1, 1, 10, 0, 0, Taiwan));

        var first = await NextAsync(provider, cancellationToken);
        var second = await NextAsync(provider, cancellationToken);

        Assert.Equal("20990101-0001", first);
        Assert.Equal("20990101-0002", second);
    }

    [Fact]
    public async Task NextAsync_OnNextDay_RestartsFromOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var day1 = CreateProvider(new DateTimeOffset(2099, 2, 1, 10, 0, 0, Taiwan)))
        {
            await NextAsync(day1, cancellationToken);
            await NextAsync(day1, cancellationToken);
        }

        await using var day2 = CreateProvider(new DateTimeOffset(2099, 2, 2, 10, 0, 0, Taiwan));
        var number = await NextAsync(day2, cancellationToken);

        Assert.Equal("20990202-0001", number);
    }

    // UTC 3/1 17:00 = 台灣 3/2 凌晨 1:00，編號必須是台灣的日期，不能是 UTC 的日期
    [Fact]
    public async Task NextAsync_BeforeEightAmTaiwanTime_UsesTaiwanDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(new DateTimeOffset(2099, 3, 1, 17, 0, 0, TimeSpan.Zero));

        var number = await NextAsync(provider, cancellationToken);

        Assert.Equal("20990302-0001", number);
    }

    // 50 個請求同時取號（而且都是當天第一次，會同時搶著建立計數器），不能有任何重複
    [Fact]
    public async Task NextAsync_ConcurrentRequests_ReturnsUniqueSequentialNumbers()
    {
        const int requestCount = 50;
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(new DateTimeOffset(2099, 4, 1, 10, 0, 0, Taiwan));

        // 每個工作用自己的 scope，模擬獨立的 HTTP 請求；DbContext 不是執行緒安全的，不能共用
        var numbers = await Task.WhenAll(
            Enumerable.Range(0, requestCount)
                .Select(_ => Task.Run(() => NextAsync(provider, cancellationToken), cancellationToken)));

        var expected = Enumerable.Range(1, requestCount).Select(n => $"20990401-{n:D4}");
        Assert.Equal(expected, numbers.Order());
    }

    // 用正式的 AddInfrastructure 註冊，只把時間來源換成固定的時間
    private static ServiceProvider CreateProvider(DateTimeOffset now)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(DatabaseFixture.ConnectionString);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        return services.BuildServiceProvider();
    }

    // 每次呼叫都開一個新的 scope，相當於一個新的請求
    private static async Task<string> NextAsync(ServiceProvider provider, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var generator = scope.ServiceProvider.GetRequiredService<ICaseNumberGenerator>();
        return await generator.NextAsync(cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
