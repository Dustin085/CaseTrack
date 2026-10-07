using CaseTrack.Application.Abstractions;
using CaseTrack.Domain.Cases;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.Infrastructure.Persistence.Repositories;

internal sealed class CaseRepository : ICaseRepository
{
    private readonly CaseTrackDbContext _dbContext;

    public CaseRepository(CaseTrackDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(Case @case)
    {
        _dbContext.Cases.Add(@case);
    }

    public Task<Case?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Cases
            .Include(c => c.SupplementRequests)
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
    }
}
