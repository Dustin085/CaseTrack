using CaseTrack.Application.Abstractions;
using CaseTrack.Infrastructure.Persistence;
using CaseTrack.Infrastructure.Persistence.Auditing;
using CaseTrack.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CaseTrack.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<CaseTrackDbContext>((sp, options) =>
            options.UseSqlServer(connectionString)
                .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

        services.AddScoped<ICaseRepository, CaseRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CaseTrackDbContext>());

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICaseNumberGenerator, SqlCaseNumberGenerator>();
        return services;
    }
}
