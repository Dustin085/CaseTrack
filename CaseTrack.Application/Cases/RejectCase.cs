using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

public sealed class RejectCaseHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RejectCaseHandler(ICaseRepository caseRepository, IUnitOfWork unitOfWork)
    {
        _caseRepository = caseRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(Guid caseId, string reason, CancellationToken cancellationToken)
    {
        var @case = await _caseRepository.GetByIdAsync(caseId, cancellationToken)
            ?? throw new NotFoundException(nameof(Case), caseId);
        @case.Reject(reason);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
