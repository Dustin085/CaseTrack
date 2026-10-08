using CaseTrack.Application.Abstractions;

namespace CaseTrack.Application.Cases;

public sealed record CaseDetails(
    Guid Id,
    string CaseNumber,
    string Subject,
    string Content,
    string Status,
    IReadOnlyList<SupplementRequestDetails> SupplementRequests);

public sealed record SupplementRequestDetails(
    int Sequence,
    string Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset? SubmittedAt);

public sealed class GetCaseHandler
{
    private readonly ICaseRepository _caseRepository;

    public GetCaseHandler(ICaseRepository caseRepository)
    {
        _caseRepository = caseRepository;
    }

    // 找不到時回傳 null，由呼叫端決定怎麼處理（API 回 404）
    public async Task<CaseDetails?> HandleAsync(Guid caseId, CancellationToken cancellationToken)
    {
        var @case = await _caseRepository.GetByIdAsync(caseId, cancellationToken);
        if (@case is null)
        {
            return null;
        }

        // 從資料庫讀出的補件紀錄沒有順序保證，回傳前依補件次序排序
        var supplementRequests = @case.SupplementRequests
            .OrderBy(r => r.Sequence)
            .Select(r => new SupplementRequestDetails(r.Sequence, r.Reason, r.RequestedAt, r.SubmittedAt))
            .ToList();

        return new CaseDetails(
            @case.Id,
            @case.CaseNumber,
            @case.Subject,
            @case.Content,
            @case.Status.ToString(),
            supplementRequests);
    }
}
