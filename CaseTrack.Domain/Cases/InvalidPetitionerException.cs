namespace CaseTrack.Domain.Cases;

public class InvalidPetitionerException : DomainValidationException
{
    public InvalidPetitionerException(string message) : base(message)
    {
    }
}
