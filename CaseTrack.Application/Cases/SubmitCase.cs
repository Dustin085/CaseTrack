using CaseTrack.Application.Abstractions;
using CaseTrack.Domain.Cases;
using System.ComponentModel.DataAnnotations;

namespace CaseTrack.Application.Cases;

public class SubmitCase
{
    public sealed record SubmitCaseCommand(string Subject, string Content);
    public sealed record SubmitCaseResult(Guid Id, string CaseNumber);
    public sealed record SubmitCaseRequest(
    [property: Required, MaxLength(200)] string Subject,
    [property: Required] string Content);

    public sealed class SubmitCaseHandler
    {
        // 建構子注入：ICaseNumberGenerator、ICaseRepository、IUnitOfWork
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
            // 1. 取號
            var caseNumber = await _caseNumberGenerator.NextAsync(cancellationToken);
            // 2. new Case(編號, command.Subject, command.Content)
            var @case = new Case(caseNumber, command.Subject, command.Content);
            // 3. repository.Add(...)
            _caseRepository.Add(@case);
            // 4. unitOfWork.SaveChangesAsync(...)
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            // 5. 回傳 new SubmitCaseResult(case.Id, case.CaseNumber)
            return new SubmitCaseResult(@case.Id, @case.CaseNumber);
        }
    }
}
