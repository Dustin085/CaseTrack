namespace CaseTrack.Domain.Cases;

public class SupplementRequest
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public int Sequence { get; private set; }
    public string Reason { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public string? SubmittedContent { get; private set; } // 還沒補件時是 null

    internal SupplementRequest(int sequence, string reason, DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Sequence = sequence;
        Reason = reason;
        RequestedAt = requestedAt;
    }

    internal void MarkSubmitted(string content, DateTimeOffset submittedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (SubmittedAt is not null)
        {
            throw new InvalidOperationException("This supplement request has already been submitted.");
        }
        if (submittedAt < RequestedAt)
        {
            throw new SubmittedAtBeforeRequestedAtException(submittedAt, RequestedAt);
        }
        SubmittedAt = submittedAt;
        SubmittedContent = content;
    }
}

