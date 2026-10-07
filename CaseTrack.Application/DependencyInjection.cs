using Microsoft.Extensions.DependencyInjection;
using static CaseTrack.Application.Cases.GetCase;
using static CaseTrack.Application.Cases.SubmitCase;

namespace CaseTrack.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<SubmitCaseHandler>();
        services.AddScoped<GetCaseHandler>();
        return services;
    }
}
