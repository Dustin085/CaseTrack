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
        // 前端顯示 detail，所以「該怎麼做」要寫在 detail；例外本身的訊息只寫進 log
        var problemDetails = new ProblemDetails
        {
            Title = "資料已被修改",
            Detail = "案件已被其他人修改，請重新整理後再試",
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
