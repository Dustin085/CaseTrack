using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Departments;
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

    // 依代碼排序：代碼唯一，所以順序固定，而且對人有意義。
    // 不用 Id：Guid 是隨機的，排出來沒有意義（SQL Server 排序 uniqueidentifier 的方式也跟 C# 不同）；
    // 不用 Name：中文的順序取決於資料庫的定序（筆畫或 Unicode 碼位），不好預測
    public async Task<IReadOnlyList<DepartmentSummary>> ListAsync(CancellationToken cancellationToken)
    {
        // 要先 await：ToListAsync 回傳 Task<List<T>>，而 Task<T> 不是 covariant，
        // 不能直接當成 Task<IReadOnlyList<T>> 回傳（List<T> 本身可以轉成 IReadOnlyList<T>）
        return await _dbContext.Departments
            .OrderBy(d => d.Code)
            .Select(d => new DepartmentSummary(
                d.Id,
                d.Code,
                d.Name))
            .ToListAsync(cancellationToken);
    }
}
