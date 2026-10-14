namespace CaseTrack.Domain.Cases;

public class InvalidPetitionerException : DomainException
{
    public InvalidPetitionerException(string message) : base(message)
    {
    }
}
