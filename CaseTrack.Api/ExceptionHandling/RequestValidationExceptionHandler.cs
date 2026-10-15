using CaseTrack.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.ExceptionHandling;

// 請求內容有問題、但要查資料庫才知道（例如單位不存在）→ 400。
// 用跟 [ApiController] 自動驗證相同的格式（ValidationProblemDetails + errors），前端處理 400 只需要一種方式
internal sealed class RequestValidationExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;

    public RequestValidationExceptionHandler(IProblemDetailsService problemDetailsService)
    {
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not RequestValidationException validationException)
        {
            return false;
        }

        var problemDetails = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            [validationException.Field] = [validationException.Message],
        })
        {
            Status = StatusCodes.Status400BadRequest
        };
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        // 使用者送錯資料是正常情況，不記錄 log
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
