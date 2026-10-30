using CertCoach.Application.Abstractions;
using CertCoach.Application.Ask;
using CertCoach.Application.Certifications;
using CertCoach.Application.Dashboard;
using CertCoach.Application.Documents;
using CertCoach.Application.Documents.Ingestion;
using CertCoach.Application.Plans;
using CertCoach.Application.Search;
using CertCoach.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CertCoach.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<UserClock>();
        services.AddScoped<CertificationService>();
        services.AddScoped<PlanService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<UserAccountService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<DocumentIngestionService>();
        services.AddScoped<SearchService>();
        services.AddScoped<AskService>();
        return services;
    }
}
