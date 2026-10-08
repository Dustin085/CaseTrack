using CaseTrack.Application.Cases;
using Microsoft.Extensions.DependencyInjection;

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
