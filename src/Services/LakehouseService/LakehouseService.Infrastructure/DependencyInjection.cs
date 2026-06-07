using Hdos.Common.Extensions;
using Hdos.LakehouseService.Application.Services;
using Hdos.LakehouseService.Infrastructure.Consumers;
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

        return services;
    }
}
