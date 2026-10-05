namespace CaseTrack.Domain.Cases
{
    public class InvalidCaseStatusTransitionException : DomainException
    {
        public CaseStatus CurrentStatus { get; }
        public CaseStatus TargetStatus { get; }
        public InvalidCaseStatusTransitionException(CaseStatus currentStatus, CaseStatus targetStatus)
            : base($"Invalid transition from {currentStatus} to {targetStatus}.")
        {
            this.CurrentStatus = currentStatus;
            this.TargetStatus = targetStatus;
        }
    }
}
