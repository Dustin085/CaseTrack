using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Common;

namespace CaseTrack.Application.Cases;

// 民眾的「我的案件」：只有自己送出的案件。
// 沿用 CaseSummary：裡面的欄位民眾都可以看。之後承辦端的摘要加了內部欄位，就要拆出民眾自己的型別
public sealed class ListMyCasesHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly ICurrentUser _currentUser;

    public ListMyCasesHandler(ICaseRepository caseRepository, ICurrentUser currentUser)
    {
        _caseRepository = caseRepository;
        _currentUser = currentUser;
    }

    // 狀態換成民眾看到的（待核定 → 審核中）。
    // 已知限制：依狀態篩選時，篩「審核中」不會包含待核定的案件（民眾端畫面目前沒有狀態篩選）
    public async Task<PagedResult<CaseSummary>> HandleAsync(CaseListQuery query, CancellationToken cancellationToken)
    {
        var result = await _caseRepository.ListAsync(
            query, CaseAccessScope.ForPetitioner(_currentUser.UserId), cancellationToken);

        return result with
        {
            Items = result.Items.Select(c => c with { Status = CitizenStatus.Of(c.Status) }).ToList(),
        };
    }
}
