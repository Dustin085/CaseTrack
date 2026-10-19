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

    public SubmitSupplementHandler(ICaseRepository caseRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _caseRepository = caseRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    // 要補的是哪一筆由 Domain 判斷（唯一一筆未補件的紀錄），呼叫端不需要指定
    public async Task HandleAsync(Guid caseId, string content, CancellationToken cancellationToken)
    {
        // 已知漏洞：民眾端還沒有身分驗證，CaseAccessScope.For(民眾) 是 None，民眾會補不了件，所以暫時用 All。
        // 任何人只要知道案件的 Id 就能補件；之後改成「案件編號 + 驗證資訊」時，改用民眾自己的範圍
        var @case = await _caseRepository.GetByIdAsync(caseId, CaseAccessScope.All, cancellationToken)
            ?? throw new NotFoundException(nameof(Case), caseId);
        @case.SubmitSupplement(content, _timeProvider.GetUtcNow());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
