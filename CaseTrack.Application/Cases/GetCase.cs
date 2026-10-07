using CaseTrack.Application.Abstractions;

namespace CaseTrack.Application.Cases;

public class GetCase
{
    public sealed record GetCaseCommand(Guid CaseId);
    public sealed record CaseDetails(
    Guid Id, string CaseNumber, string Subject, string Content, string Status,
    IReadOnlyList<SupplementRequestDetails> SupplementRequests);

    public sealed record SupplementRequestDetails(int Sequence, string Reason, DateTimeOffset RequestedAt, DateTimeOffset? SubmittedAt);

    public sealed class GetCaseHandler
    {
        // 注入 ICaseRepository
        private readonly ICaseRepository _caseRepository;

        public GetCaseHandler(ICaseRepository caseRepository)
        {
            _caseRepository = caseRepository;
        }

        // HandleAsync(Guid id, ...) → 找不到回傳 null；找到就轉換成 CaseDetails
        public async Task<CaseDetails?> HandleAsync(GetCaseCommand command, CancellationToken cancellationToken)
        {
            var @case = await _caseRepository.GetByIdAsync(command.CaseId, cancellationToken);
            if (@case is null)
            {
                return null;
            }
            var supplementRequestDetails = @case.SupplementRequests
                .Select(r => new SupplementRequestDetails(r.Sequence, r.Reason, r.RequestedAt, r.SubmittedAt))
                .ToList();
            return new CaseDetails(@case.Id,
                @case.CaseNumber,
                @case.Subject,
                @case.Content,
                @case.Status.ToString(),
                supplementRequestDetails);
        }
    }
}
