using CaseTrack.Api.Authentication;
using CaseTrack.Api.Common;
using CaseTrack.Api.Contracts;
using CaseTrack.Application.Cases;
using CaseTrack.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.Controllers;

// 承辦端的 API。民眾的送件、補件、查詢在 MyCasesController（/api/my/cases）。
// 預設拒絕：整個 Controller 都要登入，之後新增端點忘了設定 Policy，結果是「多一道登入」，而不是「少一道登入」。
// 每個端點再用 Policy 限制角色（第 1 層，403）；能碰哪些案件由 Handler 的 CaseAccessScope 決定（第 2 層，404）
[Authorize]
[ApiController]
[Route("api/cases")]
public sealed class CasesController : ControllerBase
{
    [Authorize(Policy = Policies.CaseReader)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CaseDetails>> GetCase(
        [FromRoute] Guid id,
        [FromServices] GetCaseHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        // NotFound 的情況會被拋成例外
        return Ok(result);
    }

    // 陳情人的完整個資：GET /api/cases/{id} 一律遮罩，承辦人需要聯絡民眾時才呼叫這裡，每次都會留下查詢紀錄。
    // 只有承辦人、主管（分派人員看得到案件，但不需要知道民眾是誰）。
    // no-store：個資不能被瀏覽器或中間的 proxy 快取
    [Authorize(Policy = Policies.CaseHandler)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("{id:guid}/petitioner")]
    public async Task<ActionResult<PetitionerDetails>> GetPetitioner(
        [FromRoute] Guid id,
        [FromServices] GetPetitionerHandler handler,
        CancellationToken cancellationToken)
    {
        return Ok(await handler.HandleAsync(id, cancellationToken));
    }

    [Authorize(Policy = Policies.CaseHandler)]
    [HttpPost("{id:guid}/review")]
    public async Task<ActionResult> ReviewCase(
        [FromRoute] Guid id,
        [FromServices] ReviewCaseHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = Policies.CaseHandler)]
    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult> CloseCase(
        [FromRoute] Guid id,
        [FromBody] CloseCaseRequest request,
        [FromServices] CloseCaseHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, request.Reply, cancellationToken);
        return NoContent();
    }

    // 主管核定承辦人送出的結案（/close 只是送出，核定後才結案）
    [Authorize(Policy = Policies.ClosingApprover)]
    [HttpPost("{id:guid}/closing-approval")]
    public async Task<ActionResult> ApproveClosing(
        [FromRoute] Guid id,
        [FromServices] ReviewClosingHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.ApproveAsync(id, cancellationToken);
        return NoContent();
    }

    // 主管退回結案：回到審核中，承辦人修改後重新送出
    [Authorize(Policy = Policies.ClosingApprover)]
    [HttpPost("{id:guid}/closing-return")]
    public async Task<ActionResult> ReturnClosing(
        [FromRoute] Guid id,
        [FromBody] ReturnClosingRequest request,
        [FromServices] ReviewClosingHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.ReturnAsync(id, request.Reason, cancellationToken);
        return NoContent();
    }

    // 承辦單位審核後退件。分派人員一收到就不受理的情況，用 /dismissal
    [Authorize(Policy = Policies.CaseHandler)]
    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult> RejectCase(
        [FromRoute] Guid id,
        [FromBody] RejectCaseRequest request,
        [FromServices] RejectCaseHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, request.Reason, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = Policies.CaseHandler)]
    [HttpPost("{id:guid}/supplement-requests")]
    public async Task<ActionResult> RequestSupplement(
        [FromRoute] Guid id,
        [FromBody] RequestSupplementRequest request,
        [FromServices] RequestSupplementHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, request.Reason, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = Policies.CaseDispatcher)]
    [HttpPost("{id:guid}/assignment")]
    public async Task<ActionResult> AssignDepartment(
        [FromRoute] Guid id,
        [FromBody] AssignCaseRequest request,
        [FromServices] AssignCaseHandler handler,
        CancellationToken cancellationToken)
    {
        // [Required] 讓 [ApiController] 在進入這個方法之前就擋掉 null（回 400），所以這裡一定有值。
        // 編譯器看不到這件事，用 ! 告訴它；萬一驗證被關掉，.Value 會直接拋例外，而不是默默變成 Guid.Empty
        await handler.HandleAsync(id, request.DepartmentId!.Value, cancellationToken);
        return NoContent();
    }

    // 不予受理：分派人員判斷不屬本府權責、內容無意義等，只能在分派之前（已送出）
    [Authorize(Policy = Policies.CaseDispatcher)]
    [HttpPost("{id:guid}/dismissal")]
    public async Task<ActionResult> DismissCase(
        [FromRoute] Guid id,
        [FromBody] DismissCaseRequest request,
        [FromServices] DismissCaseHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, request.Reason, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = Policies.CaseReader)]
    [HttpGet]
    public async Task<ActionResult<PagedResult<CaseSummary>>> ListCases(
        [FromQuery] ListCasesRequest request,
        [FromServices] ListCasesHandler handler,
        CancellationToken cancellationToken)
    {
        return Ok(await handler.HandleAsync(
            new CaseListQuery(request.Status, request.Keyword, request.Page, request.PageSize),
            cancellationToken));
    }

    // 下載附件。分派人員也要能看（看了照片才知道該分給哪個單位），所以用 CaseReader；
    // 能下載哪些案件的附件和 GET /api/cases/{id} 一樣，由 Handler 的資料範圍決定。
    // no-store：照片、文件可能拍到民眾的臉或門牌，不能被瀏覽器或 proxy 快取
    [Authorize(Policy = Policies.CaseReader)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<ActionResult> DownloadAttachment(
        [FromRoute] Guid id,
        [FromRoute] Guid attachmentId,
        [FromServices] GetAttachmentHandler handler,
        CancellationToken cancellationToken)
    {
        var content = await handler.ForStaffAsync(id, attachmentId, cancellationToken);
        return this.AttachmentFile(content);
    }
}
