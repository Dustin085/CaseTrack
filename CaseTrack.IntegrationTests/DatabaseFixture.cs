using CaseTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.IntegrationTests;

public class DatabaseFixture : IAsyncLifetime
{
    // 預設連 VS 使用的 LocalDB；設定環境變數 CASETRACK_TEST_CONNECTION 可以改連其他資料庫
    // （例如另一個 LocalDB 執行個體、之後的 Docker SQL Server、CI 環境）
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("CASETRACK_TEST_CONNECTION")
        ?? "Server=(localdb)\\MSSQLLocalDB;Database=CaseTrack_IntegrationTests;Trusted_Connection=True;TrustServerCertificate=True";

    public CaseTrackDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CaseTrackDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new CaseTrackDbContext(options);
    }

    public async ValueTask InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// 所有連資料庫的測試類別都加入這個 collection：
// 共用同一個 DatabaseFixture（資料庫只重建一次），而且依序執行，不會平行地互相刪除資料庫
[CollectionDefinition(nameof(DatabaseCollection))]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>;