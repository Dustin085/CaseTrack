using CaseTrack.Application.Abstractions;

namespace CaseTrack.Application.Departments;

// 單位清單：分派時的下拉選單（只列啟用中的）、列表用 DepartmentId 對照單位名稱（停用的也要對照得到）
public sealed record DepartmentSummary(
    Guid Id,
    string Code,
    string Name,
    bool IsActive);

public sealed class ListDepartmentsHandler
{
    private readonly IDepartmentRepository _departmentRepository;

    public ListDepartmentsHandler(IDepartmentRepository departmentRepository)
    {
        _departmentRepository = departmentRepository;
    }

    public Task<IReadOnlyList<DepartmentSummary>> HandleAsync(CancellationToken cancellationToken)
    {
        return _departmentRepository.ListAsync(cancellationToken);
    }
}
