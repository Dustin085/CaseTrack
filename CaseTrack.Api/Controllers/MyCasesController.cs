using CaseTrack.Api.Authentication;
using CaseTrack.Api.Contracts;
using CaseTrack.Application.Cases;
using CaseTrack.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.Controllers;

// 民眾端的 API：送件、補件、查自己的案件。
// 跟承辦端分開，Policy 加在類別上：之後新增民眾端點不會忘了限制角色，DTO 也不會跟承辦端混用。
// 只看得到自己的案件由 Handler 的 CaseAccessScope.ForPetitioner 決定，別人的案件一律 404
[Authorize(Policy = Policies.Citizen)]
[ApiController]
[Route("api/my/cases")]
public sealed class MyCasesController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SubmitCaseResult>> SubmitCase(
        [FromBody] SubmitCaseRequest request,
        [FromServices] SubmitCaseHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new SubmitCaseCommand(
                request.Subject,
                request.Content,
                request.Petitioner.Name,
                request.Petitioner.Phone,
                request.Petitioner.Email,
                request.Petitioner.Address),
            cancellationToken);

        // 201 Created + Location 標頭指向民眾自己查得到的網址（不是承辦端的 /api/cases/{id}，民眾呼叫會 403）
        return CreatedAtAction(nameof(GetMyCase), new { id = result.Id }, result);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CaseSummary>>> ListMyCases(
        [FromQuery] ListCasesRequest request,
        [FromServices] ListMyCasesHandler handler,
        CancellationToken cancellationToken)
    {
        return Ok(await handler.HandleAsync(
            new CaseListQuery(request.Status, request.Keyword, request.Page, request.PageSize),
            cancellationToken));
    }

    // 回傳完整的陳情人資料（本人的），所以跟 /api/cases/{id}/petitioner 一樣不能被快取
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CitizenCaseDetails>> GetMyCase(
        [FromRoute] Guid id,
        [FromServices] GetMyCaseHandler handler,
        CancellationToken cancellationToken)
    {
        return Ok(await handler.HandleAsync(id, cancellationToken));
    }

    // 補的是目前唯一一筆未補件的請求，所以不需要在網址指定是哪一筆
    [HttpPost("{id:guid}/supplement-submission")]
    public async Task<ActionResult> SubmitSupplement(
        [FromRoute] Guid id,
        [FromBody] SubmitSupplementRequest request,
        [FromServices] SubmitSupplementHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, request.Content, cancellationToken);
        return NoContent();
    }
}
