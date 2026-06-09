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

        // SDUI Engine — soft deprecated (doc 53 P6). Endpoint /dm/pages/{code} vẫn chạy đến P7 hard delete.
        // Page mới nên dùng DataContract layer thay vì AddSingleton<SduiPageConfig, ...>.
#pragma warning disable CS0618 // SduiPageConfig obsolete, intentional registration
        services.AddSingleton<SduiPageConfig, ExecutiveSduiConfig>();
        services.AddSingleton<SduiPageConfig, BedOccupancySduiConfig>();
        services.AddSingleton<SduiPageConfig, FinanceDailySduiConfig>();
#pragma warning restore CS0618
        services.AddSingleton<SduiEngine>();

        return services;
    }
}
