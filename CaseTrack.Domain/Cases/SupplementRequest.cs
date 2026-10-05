namespace CaseTrack.Domain.Cases
{
    public class SupplementRequest
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public int Sequence { get; private set; }
        public string Reason { get; private set; }
        public DateTimeOffset RequestedAt { get; private set; }
        public DateTimeOffset? SubmittedAt { get; private set; }

        internal SupplementRequest(int sequence, string reason, DateTimeOffset requestedAt)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Sequence = sequence;
            Reason = reason;
            RequestedAt = requestedAt;
        }

        internal void MarkSubmitted(DateTimeOffset submittedAt)
        {
            if(submittedAt < RequestedAt)
            {
                throw new SubmittedAtBeforeRequestedAtException(submittedAt, RequestedAt);
            }
            SubmittedAt = submittedAt;
        }
    }
}
