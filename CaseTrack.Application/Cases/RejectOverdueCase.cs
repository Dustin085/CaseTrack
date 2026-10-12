using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

public sealed class RejectOverdueCaseHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public RejectOverdueCaseHandler(ICaseRepository caseRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _caseRepository = caseRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task HandleAsync(Guid caseId, CancellationToken cancellationToken)
    {
        var @case = await _caseRepository.GetByIdAsync(caseId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        if (@case is null)
        {
            return;
        }
        if (!@case.IsSupplementOverdue(now))
        {
            return;
        }
        @case.RejectOverdueSupplement(now);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
