using Hdos.Common.Extensions;
using Hdos.M01Service.Domain.Repositories;
using Hdos.M01Service.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.M01Service.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddM01Infrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<M01DbContext>(opts =>
            opts.UseSqlServer(
                configuration.GetConnectionString("M01Db"),
                sql => sql.MigrationsAssembly(typeof(M01DbContext).Assembly.FullName)));

        services.AddScoped<IM01ReadRepository, M01ReadRepository>();
        services.AddScoped<IM01WriteRepository, M01WriteRepository>();
        services.AddScoped<IM01UnitOfWork, M01UnitOfWork>();

        services.AddMassTransitMessaging(configuration, x =>
        {
            x.AddEntityFrameworkOutbox<M01DbContext>(o =>
            {
                o.UseSqlServer();
                o.UseBusOutbox();
            });
        });

        return services;
    }
}
