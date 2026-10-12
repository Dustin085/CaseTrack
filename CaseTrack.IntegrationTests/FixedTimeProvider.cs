namespace CaseTrack.IntegrationTests;

// 測試用的時間來源：GetUtcNow() 永遠回傳建構時指定的時間，讓「現在」由測試控制
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}
