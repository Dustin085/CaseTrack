using CaseTrack.Application.Authorization;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Abstractions;

public interface ICaseRepository
{
    // 刻意沒有「不帶範圍」的版本，scope 也不能有預設值：每個呼叫的地方都必須決定範圍，忘記傳就編譯不過。
    // 繞過單位限制的地方明確寫 CaseAccessScope.All，搜尋它就能找到全部
    Task<Case?> GetByIdAsync(Guid id, CaseAccessScope scope, CancellationToken cancellationToken = default);

    void Add(Case @case);

    Task<IReadOnlyList<Guid>> GetOverdueSupplementCaseIdsAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken);
}
