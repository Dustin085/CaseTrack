using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

public sealed class SubmitSupplementHandler
{
    private readonly ICaseRepository _caseRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;
    private readonly AttachmentUploader _attachmentUploader;

    public SubmitSupplementHandler(ICaseRepository caseRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ICurrentUser currentUser,
        AttachmentUploader attachmentUploader)
    {
        _caseRepository = caseRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
        _attachmentUploader = attachmentUploader;
    }

    // 要補的是哪一筆由 Domain 判斷（唯一一筆未補件的紀錄），呼叫端不需要指定
    public async Task HandleAsync(Guid caseId, string content, IReadOnlyList<UploadedFile> files,
        CancellationToken cancellationToken)
    {
        // 民眾只能補自己的案件，補別人的會 404。先找案件再檢查檔案：找不到就不用讀檔頭
        var @case = await _caseRepository.GetByIdAsync(caseId, CaseAccessScope.ForPetitioner(_currentUser.UserId), cancellationToken)
            ?? throw new NotFoundException(nameof(Case), caseId);

        var newAttachments = await _attachmentUploader.InspectAsync(files, cancellationToken);
        var added = @case.SubmitSupplement(content, _timeProvider.GetUtcNow(), newAttachments);

        await _attachmentUploader.SaveThenCommitAsync(added, files, _unitOfWork.SaveChangesAsync, cancellationToken);
    }
}
