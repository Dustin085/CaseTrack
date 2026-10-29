using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Departments;

namespace CaseTrack.Application.Departments;

// 管理者維護單位：新增、改名、停用、啟用。異動由稽核攔截器自動記錄（誰、何時、改了什麼）。
// 不能刪除：舊案件的 DepartmentId 還指向它（外鍵 Restrict），而且報表、稽核紀錄都要能對照名稱
public sealed class ManageDepartmentsHandler
{
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ManageDepartmentsHandler(IDepartmentRepository departmentRepository, IUnitOfWork unitOfWork)
    {
        _departmentRepository = departmentRepository;
        _unitOfWork = unitOfWork;
    }

    // 代碼統一轉大寫：pwd 和 PWD 是同一個單位（代碼的格式由 API 的驗證限定為英數字）。
    // 先查一次給清楚的訊息；兩個人同時新增同一個代碼時，由資料庫的唯一索引擋下（最後一道防線）
    public async Task<DepartmentSummary> CreateAsync(string code, string name, CancellationToken cancellationToken)
    {
        var normalizedCode = code.ToUpperInvariant();
        if (await _departmentRepository.CodeExistsAsync(normalizedCode, cancellationToken))
        {
            throw new RequestValidationException("Code", $"代碼 {normalizedCode} 已經存在");
        }

        var department = new Department(normalizedCode, name.Trim());
        _departmentRepository.Add(department);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToSummary(department);
    }

    public Task RenameAsync(Guid id, string name, CancellationToken cancellationToken) =>
        UpdateAsync(id, d => d.Rename(name.Trim()), cancellationToken);

    public Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken) =>
        UpdateAsync(id, d =>
        {
            if (isActive)
            {
                d.Activate();
            }
            else
            {
                d.Deactivate();
            }
        }, cancellationToken);

    private async Task UpdateAsync(Guid id, Action<Department> change, CancellationToken cancellationToken)
    {
        var department = await _departmentRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Department), id);
        change(department);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static DepartmentSummary ToSummary(Department d) => new(d.Id, d.Code, d.Name, d.IsActive);
}
