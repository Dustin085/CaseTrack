using CaseTrack.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CaseTrack.Api.ExceptionHandling;

internal sealed class NotFoundExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;

    public NotFoundExceptionHandler(IProblemDetailsService problemDetailsService)
    {
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not NotFoundException notFoundException)
        {
            return false;
        }
        // detail 會直接顯示在畫面上，要是中文。例外本身的訊息（英文、含 Id）留給 log 除錯用。
        // 不分「不存在」和「沒有權限」：兩者回應完全一樣，才不會讓人試出哪些 Id 存在（IDOR）
        var problemDetails = new ProblemDetails
        {
            Title = "找不到資源",
            Detail = $"找不到這個{ResourceLabel(notFoundException.ResourceName)}",
            Status = StatusCodes.Status404NotFound
        };
        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
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

    // ResourceName 是 nameof(類別)，換成使用者看得懂的名稱；沒列到的用「資料」
    private static string ResourceLabel(string resourceName) => resourceName switch
    {
        "Case" => "案件",
        "Attachment" => "附件",
        "Department" => "單位",
        _ => "資料",
    };
}
