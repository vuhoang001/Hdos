using Hdos.Common.Extensions;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.DataMatchingService.Infrastructure.Messaging.Consumers;
using Hdos.DataMatchingService.Infrastructure.Persistence;
using Hdos.DataMatchingService.Infrastructure.Workers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.DataMatchingService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDataMatchingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<DataMatchingDbContext>((sp, opts) =>
                                                         opts.UseNpgsql(
                                                             configuration.GetConnectionString("DataMatchingDb"),
                                                             pg => pg.MigrationsAssembly(
                                                                 typeof(DataMatchingDbContext).Assembly.FullName)));

        services.AddScoped<IStagingRecordRepository, StagingRecordRepository>();
        services.AddScoped<ISourceProfileRepository, SourceProfileRepository>();
        services.AddScoped<IDataMatchingUnitOfWork, DataMatchingUnitOfWork>();

        services.AddMassTransitMessaging(configuration, x =>
        {
            x.AddEntityFrameworkOutbox<DataMatchingDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            // Consume RawRecordIngestRequestedIntegrationEvent từ source providers (doc 44).
            x.AddConsumer<RawRecordIngestRequestedConsumer>();
        }, servicePrefix: "dm");

        services.AddHostedService<MatchingWorker>();

        return services;
    }
}