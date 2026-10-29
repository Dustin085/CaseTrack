using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

// 民眾查看自己的案件。跟承辦端的 CaseDetails 分開：之後承辦端加了內部欄位（承辦人備註、內部簽核），
// 不會因為共用型別而洩漏給民眾。
// 不放單位：民眾看到 Guid 沒有意義，單位名稱之後用讀取專用的查詢（JOIN Departments）一起處理
public sealed record CitizenCaseDetails(
    Guid Id,
    string CaseNumber,
    string Subject,
    string Content,
    CaseStatus Status,
    DateTimeOffset SubmittedAt,
    string? RejectionReason,
    string? ClosingReply,
    PetitionerDetails Petitioner,
    // 送件時附的；補件附的放在各自的 SupplementRequests 裡
    IReadOnlyList<AttachmentDetails> Attachments,
    IReadOnlyList<SupplementRequestDetails> SupplementRequests);

public sealed class GetMyCaseHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly ICurrentUser _currentUser;

    public GetMyCaseHandler(ICaseRepository caseRepository, ICurrentUser currentUser)
    {
        _caseRepository = caseRepository;
        _currentUser = currentUser;
    }

    // 只找得到自己送出的案件；別人的案件跟不存在一樣，拋 NotFoundException（404），不透露案件存在
    public async Task<CitizenCaseDetails> HandleAsync(Guid caseId, CancellationToken cancellationToken)
    {
        var @case = await _caseRepository.GetByIdAsync(caseId, CaseAccessScope.ForPetitioner(_currentUser.UserId), cancellationToken)
            ?? throw new NotFoundException(nameof(Case), caseId);

        return new CitizenCaseDetails(
            @case.Id,
            @case.CaseNumber,
            @case.Subject,
            @case.Content,
            CitizenStatus.Of(@case.Status),
            @case.SubmittedAt,
            @case.RejectionReason,
            CitizenStatus.ClosingReplyOf(@case),
            ToDetails(@case.Petitioner),
            AttachmentDetails.ListFor(@case.Attachments, supplementRequestId: null),
            SupplementRequestDetails.ListFor(@case));
    }

    // 不遮罩，也不寫個資查詢紀錄：這是當事人看自己的資料。
    // 查詢紀錄要防的是公務員查看別人的個資（GetPetitionerHandler）。
    // 用 PetitionerDetails 而不是 Domain 的 Petitioner：API 的 JSON 格式不跟著 Domain 變動
    private static PetitionerDetails ToDetails(Petitioner petitioner) =>
        new(petitioner.Name, petitioner.Phone, petitioner.Email, petitioner.Address);
}
