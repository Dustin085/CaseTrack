using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

// 主管核定或退回承辦人送出的結案。範圍跟其他承辦端動作一樣用 For(currentUser)：
// 主管只看得到本單位的案件，別單位的回 404
public sealed class ReviewClosingHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public ReviewClosingHandler(ICaseRepository caseRepository, ICurrentUser currentUser, IUnitOfWork unitOfWork)
    {
        _caseRepository = caseRepository;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task ApproveAsync(Guid caseId, CancellationToken cancellationToken)
    {
        var @case = await LoadAsync(caseId, cancellationToken);
        @case.ApproveClosing(_currentUser.UserId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ReturnAsync(Guid caseId, string reason, CancellationToken cancellationToken)
    {
        var @case = await LoadAsync(caseId, cancellationToken);
        @case.ReturnClosing(reason);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Case> LoadAsync(Guid caseId, CancellationToken cancellationToken) =>
        await _caseRepository.GetByIdAsync(caseId, CaseAccessScope.For(_currentUser), cancellationToken)
            ?? throw new NotFoundException(nameof(Case), caseId);
}
