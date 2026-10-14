using CaseTrack.Domain.Cases;

namespace CaseTrack.IntegrationTests;

internal static class TestData
{
    // 值物件不可變，所有測試共用同一個實例也不會互相影響
    public static readonly Petitioner Petitioner = new("王小明", "0912345678", null, null);
}
