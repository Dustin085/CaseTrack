namespace CaseTrack.Domain.Cases;

public sealed class CaseAssignmentNotAllowedException : DomainException
{
    public CaseStatus CurrentStatus { get; }
    public CaseAssignmentNotAllowedException(CaseStatus currentStatus)
        : base($"只有已送出的案件可以分派，目前狀態：{currentStatus}")
    {
        CurrentStatus = currentStatus;
    }
}
