using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

// 民眾看到的狀態。「待核定」是機關內部的簽核流程，對民眾來說案件還在審核中：
// 顯示成審核中，民眾不會以為已經處理完，結案回覆也要等核定之後才看得到
public static class CitizenStatus
{
    public static CaseStatus Of(CaseStatus status) =>
        status == CaseStatus.PendingApproval ? CaseStatus.UnderReview : status;

    // 結案回覆要等主管核定（已結案）才給民眾看：待核定時的回覆可能被主管退回修改
    public static string? ClosingReplyOf(Case @case) =>
        @case.Status == CaseStatus.Closed ? @case.ClosingReply : null;
}
