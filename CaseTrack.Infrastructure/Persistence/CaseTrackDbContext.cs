using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;
using CaseTrack.Domain.Departments;
using CaseTrack.Infrastructure.Persistence.Auditing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.Infrastructure.Persistence;

public class CaseTrackDbContext : DbContext, IUnitOfWork
{
    public CaseTrackDbContext(DbContextOptions<CaseTrackDbContext> options) : base(options)
    {
    }

    public DbSet<Case> Cases => Set<Case>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<PersonalDataAccessEntry> PersonalDataAccessEntries => Set<PersonalDataAccessEntry>();

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
        // DbUpdateConcurrencyException 是 DbUpdateException 的子類別，要寫在前面
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("案件在讀取之後已被其他請求修改", ex);
        }
        // 2601：唯一索引的鍵值重複；2627：唯一條件約束（或主鍵）重複
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new UniqueConstraintViolationException("資料違反唯一性限制", ex);
        }
    }
}
