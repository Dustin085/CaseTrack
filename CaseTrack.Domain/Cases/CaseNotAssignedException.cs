namespace CaseTrack.Domain.Cases;

public sealed class CaseNotAssignedException : DomainException
{
    public CaseNotAssignedException()
        : base("案件尚未被分派")
    {
    }
}
