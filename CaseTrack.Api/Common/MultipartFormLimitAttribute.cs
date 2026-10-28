using CaseTrack.Domain.Cases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CaseTrack.Api.Common;

// 上傳的請求超過 [RequestSizeLimit] 時回 413（Payload Too Large），錯誤放在 Attachments 欄位。
// 不加這個的話：Kestrel 讀 body 時丟出的「太大」例外會被模型繫結吞掉，變成 400、欄位名稱是空字串、
// 英文訊息還帶著內部的數字，前端沒辦法分辨「資料填錯」和「檔案太大」。
// Resource Filter 在模型繫結之前執行：先在這裡讀一次表單，模型繫結會直接使用已經讀好的結果，不會讀兩次
[AttributeUsage(AttributeTargets.Method)]
public sealed class MultipartFormLimitAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        // 不是表單（例如送了 JSON）交給 [Consumes] 回 415
        if (!request.HasFormContentType)
        {
            await next();
            return;
        }

        try
        {
            await request.ReadFormAsync(context.HttpContext.RequestAborted);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            context.Result = PayloadTooLarge(context.HttpContext);
            return;
        }

        await next();
    }

    private static ObjectResult PayloadTooLarge(HttpContext httpContext)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Attachments",
            $"附件總大小超過上限（最多 {AttachmentRules.MaxCountPerSubmission} 個、每個 {AttachmentRules.MaxSize / 1024 / 1024} MB）");
        // 用框架的 ProblemDetailsFactory 建立：格式和其他 400 一樣（errors），也會帶上 traceId
        var problemDetails = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>()
            .CreateValidationProblemDetails(httpContext, modelState, StatusCodes.Status413PayloadTooLarge);
        return new ObjectResult(problemDetails) { StatusCode = StatusCodes.Status413PayloadTooLarge };
    }
}
