using CaseTrack.Application.Abstractions;
using CaseTrack.Domain.Cases;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.Infrastructure.Persistence;

public class CaseTrackDbContext : DbContext, IUnitOfWork
{
    public CaseTrackDbContext(DbContextOptions<CaseTrackDbContext> options) : base(options)
    {
    }

    public DbSet<Case> Cases => Set<Case>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CaseTrackDbContext).Assembly);
    }
}
