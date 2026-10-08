using CaseTrack.Application.Abstractions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

public sealed record SubmitCaseCommand(string Subject, string Content);

public sealed record SubmitCaseResult(Guid Id, string CaseNumber);

public sealed class SubmitCaseHandler
{
    private readonly ICaseNumberGenerator _caseNumberGenerator;
    private readonly ICaseRepository _caseRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SubmitCaseHandler(ICaseNumberGenerator caseNumberGenerator, ICaseRepository caseRepository, IUnitOfWork unitOfWork)
    {
        _caseNumberGenerator = caseNumberGenerator;
        _caseRepository = caseRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<SubmitCaseResult> HandleAsync(SubmitCaseCommand command, CancellationToken cancellationToken)
    {
        // 取號是獨立的交易，存檔失敗時這個編號就跳過不用（業務上允許跳號）
        var caseNumber = await _caseNumberGenerator.NextAsync(cancellationToken);
        var @case = new Case(caseNumber, command.Subject, command.Content);

        _caseRepository.Add(@case);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SubmitCaseResult(@case.Id, @case.CaseNumber);
    }
}
