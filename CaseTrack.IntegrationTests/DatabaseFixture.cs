using CaseTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.IntegrationTests;

public class DatabaseFixture : IAsyncLifetime
{
    public const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=CaseTrack_IntegrationTests;Trusted_Connection=True;TrustServerCertificate=True";

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