using System.Reflection;
using FluentValidation;
using Hdos.Common.Extensions;
using Hdos.DataMatchingService.Application.Dashboard;
using Hdos.DataMatchingService.Application.Dashboard.Configs;
using Hdos.DataMatchingService.Application.Sdui;
using Hdos.DataMatchingService.Application.Sdui.Pages;
using Hdos.DataMatchingService.Application.Services;
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

        // Unified ingest core — dùng chung cho /dm/ingest/json, /dm/ingest/file,
        // và RawRecordIngestRequestedConsumer (xem doc 44).
        services.AddScoped<IIngestCoreService, IngestCoreService>();

        // Dashboard Engine — thêm dashboard mới: AddSingleton<DashboardConfig, YourConfig>()
        services.AddSingleton<DashboardConfig, M02DashboardConfig>();
        services.AddSingleton<DashboardEngine>();

        // SDUI Engine — thêm page mới: AddSingleton<SduiPageConfig, YourPageConfig>()
        services.AddSingleton<SduiPageConfig, ExecutiveSduiConfig>();
        services.AddSingleton<SduiPageConfig, BedOccupancySduiConfig>();
        services.AddSingleton<SduiEngine>();

        return services;
    }
}
