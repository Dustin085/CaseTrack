using CaseTrack.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.Infrastructure.Persistence.Repositories;

internal sealed class DepartmentRepository : IDepartmentRepository
{
    private readonly CaseTrackDbContext _dbContext;

    public DepartmentRepository(CaseTrackDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // 只需要知道存不存在：AnyAsync 會翻譯成 SELECT CASE WHEN EXISTS(...)，不會把整筆資料讀出來
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.Departments.AnyAsync(d => d.Id == id, cancellationToken);
    }
}
