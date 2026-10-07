using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Abstractions;

public interface ICaseRepository
{
    Task<Case?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Case @case);
}
