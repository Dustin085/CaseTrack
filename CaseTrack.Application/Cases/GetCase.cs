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
    string Status,
    DateTimeOffset SubmittedAt,
    Guid? DepartmentId,
    string? RejectionReason,
    string? ClosingReply,
    PetitionerDetails Petitioner,
    IReadOnlyList<SupplementRequestDetails> SupplementRequests);

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
    string? SubmittedContent);

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

        // 從資料庫讀出的補件紀錄沒有順序保證，回傳前依補件次序排序
        var supplementRequests = @case.SupplementRequests
            .OrderBy(r => r.Sequence)
            .Select(r => new SupplementRequestDetails(r.Sequence, r.Reason, r.RequestedAt, r.SubmittedAt, r.SubmittedContent))
            .ToList();

        return new CaseDetails(
            @case.Id,
            @case.CaseNumber,
            @case.Subject,
            @case.Content,
            @case.Status.ToString(),
            @case.SubmittedAt,
            @case.DepartmentId,
            @case.RejectionReason,
            @case.ClosingReply,
            ToMaskedDetails(@case.Petitioner),
            supplementRequests);
    }

    // 目前還沒有登入功能，所以一律遮罩（比較安全的預設值）。
    // 加上授權之後要改成依照查看的人決定：承辦人、單位主管看完整資料（並寫入個資查詢紀錄），其他人看遮罩後的值。
    // 這是業務規則加上授權，所以放在 Application，不放在 Controller。
    private static PetitionerDetails ToMaskedDetails(Petitioner petitioner) =>
        new(
            PersonalDataMasker.Mask(petitioner.Name),
            PersonalDataMasker.Mask(petitioner.Phone),
            PersonalDataMasker.Mask(petitioner.Email),
            PersonalDataMasker.Mask(petitioner.Address));
}
