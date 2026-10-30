using CertCoach.Application.Abstractions;
using CertCoach.Application.Certifications;
using CertCoach.Application.Dashboard;
using CertCoach.Application.Plans;
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
        return services;
    }
}
