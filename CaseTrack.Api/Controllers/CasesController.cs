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
        return CreatedAtAction(nameof(GetCase), new { caseId = result.Id }, result);
    }

    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<CaseDetails>> GetCase(
        [FromRoute] Guid caseId,
        [FromServices] GetCaseHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(caseId, cancellationToken);

        // 不帶內容的 NotFound()，[ApiController] 會自動轉成 ProblemDetails，跟 400 的格式一致
        return result is null ? NotFound() : Ok(result);
    }
}
