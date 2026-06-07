using Hdos.Common.Extensions;
using Hdos.LakehouseService.Infrastructure.Consumers;
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

        return services;
    }
}
