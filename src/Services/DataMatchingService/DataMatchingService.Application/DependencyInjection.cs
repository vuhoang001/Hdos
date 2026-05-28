using System.Reflection;
using FluentValidation;
using Hdos.Common.Extensions;
using Hdos.DataMatchingService.Application.Dashboard;
using Hdos.DataMatchingService.Application.Dashboard.Configs;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.DataMatchingService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDataMatchingApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddCommonMediatRBehaviors();

        // Dashboard Engine — thêm dashboard mới: AddSingleton<DashboardConfig, YourConfig>()
        services.AddSingleton<DashboardConfig, M02DashboardConfig>();
        services.AddSingleton<DashboardEngine>();

        return services;
    }
}
