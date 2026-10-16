using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

public sealed class RejectCaseHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public RejectCaseHandler(ICaseRepository caseRepository, ICurrentUser currentUser, IUnitOfWork unitOfWork)
    {
        _caseRepository = caseRepository;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(Guid caseId, string reason, CancellationToken cancellationToken)
    {
        var @case = await _caseRepository.GetByIdAsync(caseId, CaseAccessScope.For(_currentUser), cancellationToken)
            ?? throw new NotFoundException(nameof(Case), caseId);
        @case.Reject(reason);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
