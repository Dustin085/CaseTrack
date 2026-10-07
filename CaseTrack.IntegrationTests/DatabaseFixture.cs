using CaseTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.IntegrationTests;

public class DatabaseFixture : IAsyncLifetime
{
    private const string ConnectionString =
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