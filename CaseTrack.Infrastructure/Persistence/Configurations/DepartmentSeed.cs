namespace CaseTrack.Infrastructure.Persistence.Configurations;

public static class DepartmentSeed
{
    public static readonly Guid PublicWorks = Guid.Parse("f3c0543e-db7c-464b-8016-d71088ef6b83");          // 工務局 PWD
    public static readonly Guid EnvironmentalProtection = Guid.Parse("05e97217-8eee-4551-8ff5-5bd9cb1762d0"); // 環境保護局 ENV
    public static readonly Guid Transportation = Guid.Parse("2f0c5ba0-9486-44b7-ab5b-a7032e4dc493");       // 交通局 TRA
}
