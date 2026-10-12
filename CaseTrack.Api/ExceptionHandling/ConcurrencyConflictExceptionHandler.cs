using CaseTrack.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.ExceptionHandling;

internal sealed class ConcurrencyConflictExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<ConcurrencyConflictExceptionHandler> _logger;

    public ConcurrencyConflictExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<ConcurrencyConflictExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ConcurrencyConflictException concurrencyConflictException)
        {
            return false;
        }
        var problemDetails = new ProblemDetails
        {
            Title = "案件已被其他人修改，請重新整理後再試",
            Detail = concurrencyConflictException.Message,
            Status = StatusCodes.Status409Conflict
        };
        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        _logger.LogWarning(exception, "Concurrency conflict exception occurred :{Message}", exception.Message);
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
