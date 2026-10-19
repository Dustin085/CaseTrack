using CaseTrack.Application.Abstractions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

public sealed record SubmitCaseCommand(
    string Subject,
    string Content,
    string? PetitionerName,
    string? PetitionerPhone,
    string? PetitionerEmail,
    string? PetitionerAddress);

public sealed record SubmitCaseResult(Guid Id, string CaseNumber);

public sealed class SubmitCaseHandler
{
    private readonly ICaseNumberGenerator _caseNumberGenerator;
    private readonly ICaseRepository _caseRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public SubmitCaseHandler(ICaseNumberGenerator caseNumberGenerator,
        ICaseRepository caseRepository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        _caseNumberGenerator = caseNumberGenerator;
        _caseRepository = caseRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public async Task<SubmitCaseResult> HandleAsync(SubmitCaseCommand command, CancellationToken cancellationToken)
    {
        // 先建立陳情人：資料不合法就直接失敗，不浪費一個案件編號
        var petitioner = new Petitioner(
            command.PetitionerName, command.PetitionerPhone, command.PetitionerEmail, command.PetitionerAddress);

        // 取號是獨立的交易，存檔失敗時這個編號就跳過不用（業務上允許跳號）
        var caseNumber = await _caseNumberGenerator.NextAsync(cancellationToken);
        var @case = new Case(caseNumber, command.Subject, command.Content, petitioner,
            _currentUser.UserId, _timeProvider.GetUtcNow());

        _caseRepository.Add(@case);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SubmitCaseResult(@case.Id, @case.CaseNumber);
    }
}
