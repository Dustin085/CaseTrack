using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

// 下載用：Content 由呼叫端負責 Dispose（Controller 交給 File()，框架傳送完會自動 Dispose）
public sealed record AttachmentContent(string FileName, string ContentType, Stream Content);

// 下載附件。民眾和承辦端各一個入口，資料範圍在這裡決定（不讓 Controller 傳進來），和 GetCaseHandler、GetMyCaseHandler 一樣
public sealed class GetAttachmentHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;

    public GetAttachmentHandler(ICaseRepository caseRepository, ICurrentUser currentUser, IFileStorage fileStorage)
    {
        _caseRepository = caseRepository;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
    }

    // 只找得到自己送出的案件的附件
    public Task<AttachmentContent> ForPetitionerAsync(Guid caseId, Guid attachmentId, CancellationToken cancellationToken) =>
        HandleAsync(caseId, attachmentId, CaseAccessScope.ForPetitioner(_currentUser.UserId), cancellationToken);

    // 依角色的資料範圍（承辦人、主管：本單位；分派人員：全部）
    public Task<AttachmentContent> ForStaffAsync(Guid caseId, Guid attachmentId, CancellationToken cancellationToken) =>
        HandleAsync(caseId, attachmentId, CaseAccessScope.For(_currentUser), cancellationToken);

    private async Task<AttachmentContent> HandleAsync(
        Guid caseId,
        Guid attachmentId,
        CaseAccessScope scope,
        CancellationToken cancellationToken)
    {
        var @case = await _caseRepository.GetByIdAsync(caseId, scope, cancellationToken)
            ?? throw new NotFoundException(nameof(Case), caseId);

        // 一定要從「這個案件」的附件裡找，不能直接用 attachmentId 查：
        // 否則拿自己的案件 Id 配上別人的附件 Id，資料範圍的檢查會通過，卻下載到別人的檔案（IDOR）
        var attachment = @case.Attachments.SingleOrDefault(a => a.Id == attachmentId)
            ?? throw new NotFoundException(nameof(Attachment), attachmentId);

        // 資料庫有紀錄、檔案卻不見了：先存檔再存資料庫，正常情況不會發生，所以是系統的問題
        // （有人手動刪了檔案、NAS 沒掛上、還原不完整）。回 500 並記錄 log，讓維運人員知道；回 404 會把問題掩蓋掉
        var content = await _fileStorage.OpenReadAsync(attachment.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Attachment '{attachment.Id}' of case '{caseId}' has a record but no stored file.");

        return new AttachmentContent(attachment.FileName, attachment.ContentType, content);
    }
}
