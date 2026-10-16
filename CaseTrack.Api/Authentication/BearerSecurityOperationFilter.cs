using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CaseTrack.Api.Authentication;

// 只在需要登入的端點標上 Bearer token（Swagger UI 會顯示鎖頭，呼叫時帶上 Authorize 輸入的 token）。
// 不用全域的 AddSecurityRequirement：那樣送件、民眾補件這些匿名端點也會顯示鎖頭，文件會誤導人
internal sealed class BearerSecurityOperationFilter : IOperationFilter
{
    public const string SchemeId = "Bearer";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var requiresAuthentication = metadata.OfType<IAuthorizeData>().Any()
            && !metadata.OfType<IAllowAnonymous>().Any();
        if (!requiresAuthentication)
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeId, context.Document)] = [],
        });
    }
}
