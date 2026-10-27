namespace CaseTrack.Domain.Cases;

public class InvalidAttachmentException : DomainValidationException
{
    public InvalidAttachmentException(string message) : base(message)
    {
    }
}
