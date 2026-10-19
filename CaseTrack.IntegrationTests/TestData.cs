using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure.Persistence.Configurations;

namespace CaseTrack.IntegrationTests;

internal static class TestData
{
    // 值物件不可變，所有測試共用同一個實例也不會互相影響
    public static readonly Petitioner Petitioner = new("王小明", "0912345678", null, null);

    // 送件的民眾和時間：只是測試資料，測試不依賴它們的值
    public const string SubmittedBy = "u-citizen-1";
    public static readonly DateTimeOffset SubmittedAt = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(8));

    // migration 預先放入的工務局：受理之前一定要先分派，而且外鍵要求單位必須存在
    public static readonly Guid DepartmentId = DepartmentSeed.PublicWorks;
}
