using CaseTrack.Application.Abstractions;

namespace CaseTrack.Infrastructure.Persistence.Auditing;

internal sealed class PersonalDataAccessLog : IPersonalDataAccessLog
{
    private readonly CaseTrackDbContext _dbContext;

    public PersonalDataAccessLog(CaseTrackDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Record(Guid caseId, string accessedBy, DateTimeOffset accessedAt)
    {
        _dbContext.PersonalDataAccessEntries.Add(new PersonalDataAccessEntry(caseId, accessedBy, accessedAt));
    }
}
