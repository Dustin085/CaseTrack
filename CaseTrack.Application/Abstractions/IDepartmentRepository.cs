namespace CaseTrack.Application.Abstractions;

public interface IDepartmentRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken); 
}
