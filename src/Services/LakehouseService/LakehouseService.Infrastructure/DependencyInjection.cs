using Hdos.Common.Extensions;
using Hdos.LakehouseService.Domain.Repositories;
using Hdos.LakehouseService.Infrastructure.Consumers;
using Hdos.LakehouseService.Infrastructure.Persistence;
using Hdos.LakehouseService.Infrastructure.Persistence.Repositories;
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

        services.AddScoped<ILakehouseSnapshotRepository, LakehouseSnapshotRepository>();

        services.AddMassTransitMessaging(configuration, x =>
        {
            x.AddConsumer<LakehouseDataReadyConsumer>();
        }, servicePrefix: "lh",
           externalConsumersAssembly: typeof(DependencyInjection).Assembly);

        return services;
    }
}
