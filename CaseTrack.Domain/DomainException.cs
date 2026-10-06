namespace CaseTrack.Domain;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}
