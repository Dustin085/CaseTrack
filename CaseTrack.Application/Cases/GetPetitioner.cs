using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

// 顯示陳情人的完整個資（GET /api/cases/{id} 一律遮罩）。
// 承辦人需要聯絡民眾時才呼叫，而且每次都會留下查詢紀錄
public sealed class GetPetitionerHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IPersonalDataAccessLog _accessLog;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public GetPetitionerHandler(ICaseRepository caseRepository,
        ICurrentUser currentUser,
        IPersonalDataAccessLog accessLog,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _caseRepository = caseRepository;
        _currentUser = currentUser;
        _accessLog = accessLog;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<PetitionerDetails> HandleAsync(Guid caseId, CancellationToken cancellationToken)
    {
        var @case = await _caseRepository.GetByIdAsync(caseId, CaseAccessScope.For(_currentUser), cancellationToken)
            ?? throw new NotFoundException(nameof(Case), caseId);

        // 沒有紀錄就不准看：先寫入查詢紀錄並存檔成功，才回傳完整資料。
        // 存檔失敗會拋例外，呼叫端拿不到任何個資
        _accessLog.Record(@case.Id, _currentUser.UserId, _timeProvider.GetUtcNow());
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var petitioner = @case.Petitioner;
        return new PetitionerDetails(petitioner.Name, petitioner.Phone, petitioner.Email, petitioner.Address);
    }
}
