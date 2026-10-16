using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

public sealed class AssignCaseHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AssignCaseHandler(ICaseRepository caseRepository,
        ICurrentUser currentUser,
        IDepartmentRepository departmentRepository,
        IUnitOfWork unitOfWork)
    {
        _caseRepository = caseRepository;
        _currentUser = currentUser;
        _departmentRepository = departmentRepository;
        _unitOfWork = unitOfWork;
    }

    // 先查案件再查單位：案件不存在的話，單位對不對已經不重要了，先回報最根本的問題（404）
    public async Task HandleAsync(Guid caseId, Guid departmentId, CancellationToken cancellationToken)
    {
        var @case = await _caseRepository.GetByIdAsync(caseId, CaseAccessScope.For(_currentUser), cancellationToken)
            ?? throw new NotFoundException(nameof(Case), caseId);

        // 單位是請求內容裡的資料，不存在是「送來的資料有問題」→ 400，不是 404（404 是網址裡的案件不存在）。
        // 外鍵仍然保留：查詢和存檔之間單位被刪掉的話，由資料庫擋下（最後一道防線）。
        // 欄位名稱跟 [ApiController] 驗證 AssignCaseRequest 時一致，前端只要對應一個 key
        if (!await _departmentRepository.ExistsAsync(departmentId, cancellationToken))
        {
            throw new RequestValidationException("DepartmentId", "單位不存在");
        }

        @case.AssignTo(departmentId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
