using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.Infrastructure.Persistence;

public class CaseTrackDbContext : DbContext, IUnitOfWork
{
    public CaseTrackDbContext(DbContextOptions<CaseTrackDbContext> options) : base(options)
    {
    }

    public DbSet<Case> Cases => Set<Case>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CaseTrackDbContext).Assembly);
    }

    async Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("案件在讀取之後已被其他請求修改", ex);
        }
    }
}
