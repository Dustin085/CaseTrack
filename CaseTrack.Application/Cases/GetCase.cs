using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Exceptions;
using CaseTrack.Application.Privacy;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

public sealed record CaseDetails(
    Guid Id,
    string CaseNumber,
    string Subject,
    string Content,
    CaseStatus Status,
    DateTimeOffset SubmittedAt,
    Guid? DepartmentId,
    string? RejectionReason,
    string? ClosingReply,
    PetitionerDetails Petitioner,
    // 送件時附的；補件附的放在各自的 SupplementRequests 裡
    IReadOnlyList<AttachmentDetails> Attachments,
    IReadOnlyList<SupplementRequestDetails> SupplementRequests,
    // 目前的使用者現在能做哪些動作（見 CaseActions），前端照著顯示按鈕
    IReadOnlyList<CaseAction> AllowedActions);

public sealed record PetitionerDetails(
    string? Name,
    string? Phone,
    string? Email,
    string? Address);

public sealed record SupplementRequestDetails(
    int Sequence,
    string Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset? SubmittedAt,
    string? SubmittedContent,
    // 這一次補件附的。在後端就分好組，前端不用自己對應（DTO 也就不需要補件的 Id）
    IReadOnlyList<AttachmentDetails> Attachments)
{
    // 承辦端和民眾端共用。從資料庫讀出的補件紀錄沒有順序保證，依補件次序排序
    internal static IReadOnlyList<SupplementRequestDetails> ListFor(Case @case) =>
        @case.SupplementRequests
            .OrderBy(r => r.Sequence)
            .Select(r => new SupplementRequestDetails(
                r.Sequence,
                r.Reason,
                r.RequestedAt,
                r.SubmittedAt,
                r.SubmittedContent,
                AttachmentDetails.ListFor(@case.Attachments, r.Id)))
            .ToList();
}

public sealed class GetCaseHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly ICurrentUser _currentUser;

    public GetCaseHandler(ICaseRepository caseRepository, ICurrentUser currentUser)
    {
        _caseRepository = caseRepository;
        _currentUser = currentUser;
    }

    // 找不到時拋 NotFoundException
    public async Task<CaseDetails> HandleAsync(Guid caseId, CancellationToken cancellationToken)
    {
        var @case = await _caseRepository.GetByIdAsync(caseId, CaseAccessScope.For(_currentUser), cancellationToken)
            ?? throw new NotFoundException(nameof(Case), caseId);

        return new CaseDetails(
            @case.Id,
            @case.CaseNumber,
            @case.Subject,
            @case.Content,
            @case.Status,
            @case.SubmittedAt,
            @case.DepartmentId,
            @case.RejectionReason,
            @case.ClosingReply,
            ToMaskedDetails(@case.Petitioner),
            AttachmentDetails.ListFor(@case.Attachments, supplementRequestId: null),
            SupplementRequestDetails.ListFor(@case),
            CaseActions.AllowedFor(@case, _currentUser));
    }

    // 一律遮罩：大部分時候只需要看陳情內容。承辦人、主管需要聯絡民眾時，另外呼叫 GET /api/cases/{id}/petitioner
    // 取得完整資料，每次都會留下個資查詢紀錄（GetPetitionerHandler）
    private static PetitionerDetails ToMaskedDetails(Petitioner petitioner) =>
        new(
            PersonalDataMasker.Mask(petitioner.Name),
            PersonalDataMasker.Mask(petitioner.Phone),
            PersonalDataMasker.Mask(petitioner.Email),
            PersonalDataMasker.Mask(petitioner.Address));
}
