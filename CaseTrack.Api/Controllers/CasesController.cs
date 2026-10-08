using CaseTrack.Api.Contracts;
using CaseTrack.Application.Cases;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.Controllers;

[ApiController]
[Route("api/cases")]
public sealed class CasesController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SubmitCaseResult>> SubmitCase(
        [FromBody] SubmitCaseRequest request,
        [FromServices] SubmitCaseHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new SubmitCaseCommand(request.Subject, request.Content),
            cancellationToken);

        // 201 Created + Location 標頭指向新案件的網址
        return CreatedAtAction(nameof(GetCase), new { id = result.Id }, result);
    }

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

    [HttpPost("{id:guid}/review")]
    public async Task<ActionResult> ReviewCase(
        [FromRoute] Guid id,
        [FromServices] ReviewCaseHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult> CloseCase(
        [FromRoute] Guid id,
        [FromServices] CloseCaseHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }

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

    // 民眾補件：補的是目前唯一一筆未補件的請求，所以不需要在網址指定是哪一筆
    [HttpPost("{id:guid}/supplement-submission")]
    public async Task<ActionResult> SubmitSupplement(
        [FromRoute] Guid id,
        [FromServices] SubmitSupplementHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }
}
