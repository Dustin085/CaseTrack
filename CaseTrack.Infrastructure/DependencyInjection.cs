using CaseTrack.Application.Abstractions;
using CaseTrack.Infrastructure.Persistence;
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
        services.AddDbContext<CaseTrackDbContext>(options =>
            options.UseSqlServer(connectionString));
        services.AddScoped<ICaseRepository, CaseRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CaseTrackDbContext>());
        return services;
    }
}
