using CaseTrack.Application.Departments;

namespace CaseTrack.Application.Abstractions;

public interface IDepartmentRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DepartmentSummary>> ListAsync(
        CancellationToken cancellationToken);
}
