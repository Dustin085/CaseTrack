using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

public sealed class SubmitSupplementHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public SubmitSupplementHandler(ICaseRepository caseRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ICurrentUser currentUser)
    {
        _caseRepository = caseRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    // 要補的是哪一筆由 Domain 判斷（唯一一筆未補件的紀錄），呼叫端不需要指定
    public async Task HandleAsync(Guid caseId, string content, CancellationToken cancellationToken)
    {
        // 民眾只能補自己的案件，補別人的會 404
        var @case = await _caseRepository.GetByIdAsync(caseId, CaseAccessScope.ForPetitioner(_currentUser.UserId), cancellationToken)
            ?? throw new NotFoundException(nameof(Case), caseId);
        @case.SubmitSupplement(content, _timeProvider.GetUtcNow(), []);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
