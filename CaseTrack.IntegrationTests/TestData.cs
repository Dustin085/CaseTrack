using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure.Persistence.Configurations;

namespace CaseTrack.IntegrationTests;

internal static class TestData
{
    // 值物件不可變，所有測試共用同一個實例也不會互相影響
    public static readonly Petitioner Petitioner = new("王小明", "0912345678", null, null);

    // migration 預先放入的工務局：受理之前一定要先分派，而且外鍵要求單位必須存在
    public static readonly Guid DepartmentId = DepartmentSeed.PublicWorks;
}
