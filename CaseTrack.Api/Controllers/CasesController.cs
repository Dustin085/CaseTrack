using Microsoft.AspNetCore.Mvc;
using static CaseTrack.Application.Cases.GetCase;
using static CaseTrack.Application.Cases.SubmitCase;

namespace CaseTrack.Api.Controllers;

[ApiController]
[Route("/api/cases")]
public class CasesController : ControllerBase
{
    [HttpPost]
    public async Task<SubmitCaseResult> SubmitCase(
        [FromBody] SubmitCaseRequest request,
        [FromServices] SubmitCaseHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new SubmitCaseCommand(request.Subject, request.Content),
            cancellationToken
            );
        return result;
    }

    [HttpGet("{caseId:guid}")]
    public async Task<CaseDetails> GetCase(
        [FromRoute] Guid caseId,
        [FromServices] GetCaseHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new GetCaseCommand(caseId),
            cancellationToken
        );
        return result;
    }
}
