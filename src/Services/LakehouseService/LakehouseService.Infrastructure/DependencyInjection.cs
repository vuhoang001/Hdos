using Hdos.Common.Extensions;
using Hdos.LakehouseService.Application.Services;
using Hdos.LakehouseService.Infrastructure.Charts;
using Hdos.LakehouseService.Infrastructure.Charts.Configs;
using Hdos.LakehouseService.Infrastructure.Consumers;
using Hdos.LakehouseService.Infrastructure.DataContracts.Registration;
using Hdos.LakehouseService.Infrastructure.ExternalClients;
using Hdos.LakehouseService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.LakehouseService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLakehouseInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<LakehouseDbContext>((_, opts) =>
            opts.UseNpgsql(
                configuration.GetConnectionString("LakehouseDb"),
                pg => pg.MigrationsAssembly(typeof(LakehouseDbContext).Assembly.FullName)));

        services.AddMassTransitMessaging(configuration, x =>
        {
            // DE pipeline báo "data ready" → trigger sync các ViewBinding tương ứng (doc 44 §6).
            x.AddConsumer<WarehouseRefreshedConsumer>();
        }, servicePrefix: "lh",
           externalConsumersAssembly: typeof(DependencyInjection).Assembly);

        // Cross-service HTTP client gọi DataMatching để enroll SourceProfile (doc 45 §6.4.4 + §7).
        services.AddHttpClient<ISourceProfileEnrollClient, SourceProfileEnrollClient>(c =>
        {
            c.BaseAddress = new Uri(
                configuration["Services:DataMatching:BaseUrl"]
                    ?? "http://datamatchingservice:8080");
            c.Timeout = TimeSpan.FromSeconds(10);
        });

        // Lakehouse Charts — soft deprecated (doc 53 P6). Endpoint /lakehouse/charts/{code} vẫn chạy đến P7.
        // Chart mới nên dùng DataContract layer thay vì AddSingleton<ILakehouseChartConfig, ...>.
#pragma warning disable CS0618 // ILakehouseChartConfig obsolete, intentional registration
        services.AddSingleton<ILakehouseChartConfig, BedOccupancyLakehouseChart>();
        services.AddSingleton<ILakehouseChartConfig, FinanceDailyLakehouseChart>();
#pragma warning restore CS0618
        services.AddSingleton<LakehouseChartBuilder>();

        // Data Contract layer (doc 53) — chạy song song với LakehouseChartBuilder ở trên.
        // Endpoint cũ /lakehouse/charts/{code} vẫn dùng builder cũ; endpoint mới
        // /lakehouse/contracts/{code}/chart dùng DataContractGateway.
        services.AddLakehouseDataContracts();

        return services;
    }
}
