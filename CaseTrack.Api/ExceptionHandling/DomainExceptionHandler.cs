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
        // 資料不合法 → 400；跟目前的狀態衝突 → 409。
        // 依基底類別判斷，不逐一列出例外型別：Domain 新增驗證類的例外時，這裡不用改。
        var (status, title) = domainException is DomainValidationException
            ? (StatusCodes.Status400BadRequest, "資料不符合規定")
            : (StatusCodes.Status409Conflict, "此操作不符合案件目前的狀態");

        var problemDetails = new ProblemDetails
        {
            Title = title,
            Detail = domainException.Message,
            Status = status
        };
        httpContext.Response.StatusCode = status;

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
