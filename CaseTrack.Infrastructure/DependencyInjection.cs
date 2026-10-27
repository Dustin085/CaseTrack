using CaseTrack.Application.Abstractions;
using CaseTrack.Infrastructure.Persistence;
using CaseTrack.Infrastructure.Persistence.Auditing;
using CaseTrack.Infrastructure.Persistence.Repositories;
using CaseTrack.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CaseTrack.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string fileStorageRootPath)
    {
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<CaseTrackDbContext>((sp, options) =>
            options.UseSqlServer(connectionString)
                .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

        services.AddScoped<ICaseRepository, CaseRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IPersonalDataAccessLog, PersonalDataAccessLog>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CaseTrackDbContext>());

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICaseNumberGenerator, SqlCaseNumberGenerator>();

        // 沒有狀態（只有根目錄這個設定），整個 app 共用一個
        services.AddSingleton<IFileStorage>(new LocalFileStorage(fileStorageRootPath));
        return services;
    }
}
