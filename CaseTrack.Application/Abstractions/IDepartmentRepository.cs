using CaseTrack.Application.Departments;
using CaseTrack.Domain.Departments;

namespace CaseTrack.Application.Abstractions;

public interface IDepartmentRepository
{
    // 存在而且啟用中：只有這種單位可以分派案件
    Task<bool> IsActiveAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(Department department);

    // 包含停用的單位：列表要能顯示舊案件的單位名稱，下拉選單由前端只列啟用中的
    Task<IReadOnlyList<DepartmentSummary>> ListAsync(
        CancellationToken cancellationToken);
}
