using CaseTrack.Domain;
using CaseTrack.Domain.Cases;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.ExceptionHandling;

internal sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(IProblemDetailsService problemDetailsService,
        ILogger<DomainExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }
        var problemDetails = new ProblemDetails
        {
            Title = "此操作不符合案件目前的狀態",
            Detail = domainException.Message,
            Status = StatusCodes.Status409Conflict
        };
        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        if (exception is InvalidCaseStatusTransitionException transitionException)
        {
            problemDetails.Extensions["currentStatus"] = transitionException.CurrentStatus.ToString();
            problemDetails.Extensions["targetStatus"] = transitionException.TargetStatus.ToString();
        }
        if (exception is SubmittedAtBeforeRequestedAtException submittedAtBeforeRequestedAtException)
        {
            problemDetails.Extensions["submittedAt"] = submittedAtBeforeRequestedAtException.SubmittedAt.ToString("o");
            problemDetails.Extensions["requestedAt"] = submittedAtBeforeRequestedAtException.RequestedAt.ToString("o");
        }
        _logger.LogWarning(exception, "Domain exception occurred: {Message}", exception.Message);
        await _problemDetailsService.TryWriteAsync(
                new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    ProblemDetails = problemDetails,
                    Exception = exception
                }
            );
        return true;
    }
}
