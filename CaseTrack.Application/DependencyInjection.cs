using CaseTrack.Application.Auditing;
using CaseTrack.Application.Cases;
using CaseTrack.Application.Departments;
using CaseTrack.Application.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace CaseTrack.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AttachmentUploader>();
        services.AddScoped<SubmitCaseHandler>();
        services.AddScoped<GetCaseHandler>();
        services.AddScoped<ReviewCaseHandler>();
        services.AddScoped<RejectCaseHandler>();
        services.AddScoped<CloseCaseHandler>();
        services.AddScoped<ReviewClosingHandler>();
        services.AddScoped<CaseReportHandler>();
        services.AddScoped<SubmitSupplementHandler>();
        services.AddScoped<RequestSupplementHandler>();
        services.AddScoped<RejectOverdueCaseHandler>();
        services.AddScoped<AssignCaseHandler>();
        services.AddScoped<DismissCaseHandler>();
        services.AddScoped<GetPetitionerHandler>();
        services.AddScoped<ListCasesHandler>();
        services.AddScoped<ListMyCasesHandler>();
        services.AddScoped<GetMyCaseHandler>();
        services.AddScoped<GetAttachmentHandler>();
        services.AddScoped<ListDepartmentsHandler>();
        services.AddScoped<ManageDepartmentsHandler>();
        services.AddScoped<AuditLogHandler>();
        return services;
    }
}
