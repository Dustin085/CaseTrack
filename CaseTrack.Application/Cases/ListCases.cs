using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Common;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

// 查詢條件。Page 是給人看的頁碼（從 1 開始），Repository 再換算成要跳過幾筆
public sealed record CaseListQuery(CaseStatus? Status, string? Keyword, int Page, int PageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

// 列表的每一筆：刻意不包含陳情人和陳情內容。列表一次顯示很多筆，就算遮罩過也是大量暴露個資，要看再點進詳情
public sealed record CaseSummary(
    Guid Id,
    string CaseNumber,
    string Subject,
    CaseStatus Status,
    DateTimeOffset SubmittedAt,
    Guid? DepartmentId);

public sealed class ListCasesHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly ICurrentUser _currentUser;

    public ListCasesHandler(ICaseRepository caseRepository, ICurrentUser currentUser)
    {
        _caseRepository = caseRepository;
        _currentUser = currentUser;
    }

    public Task<PagedResult<CaseSummary>> HandleAsync(CaseListQuery query, CancellationToken cancellationToken)
    {
        return _caseRepository.ListAsync(query, CaseAccessScope.For(_currentUser), cancellationToken);
    }
}
