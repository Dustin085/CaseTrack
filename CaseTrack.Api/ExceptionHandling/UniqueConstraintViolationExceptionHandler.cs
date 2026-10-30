using CaseTrack.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.ExceptionHandling;

// 沒有被 Handler 轉成欄位錯誤的唯一性衝突（例如單位代碼由 ManageDepartmentsHandler 轉成 400）的最後一層：
// 回 409，不要變成 500。會走到這裡代表有一個唯一索引沒有對應的友善訊息，記 Warning 方便發現
internal sealed class UniqueConstraintViolationExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<UniqueConstraintViolationExceptionHandler> _logger;

    public UniqueConstraintViolationExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<UniqueConstraintViolationExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not UniqueConstraintViolationException)
        {
            return false;
        }
        var problemDetails = new ProblemDetails
        {
            Title = "資料重複",
            Detail = "資料已經存在，請重新整理後再試",
            Status = StatusCodes.Status409Conflict
        };
        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        _logger.LogWarning(exception, "Unique constraint violation: {Message}", exception.InnerException?.Message);
        await _problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails,
                Exception = exception
            });
        return true;
    }
}
