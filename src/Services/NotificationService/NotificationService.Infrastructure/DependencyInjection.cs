using Hdos.Common.Extensions;
using Hdos.Common.Persistence;
using Hdos.NotificationService.Domain.Repositories;
using Hdos.NotificationService.Infrastructure.Consumers;
using Hdos.NotificationService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.NotificationService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDomainEventDispatching();

        services.AddDbContext<NotificationDbContext>((sp, opts) =>
            opts.UseSqlServer(
                    configuration.GetConnectionString("NotificationDb"),
                    sql => sql.MigrationsAssembly(typeof(NotificationDbContext).Assembly.FullName))
                .AddInterceptors(sp.GetRequiredService<PublishDomainEventsInterceptor>()));

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddRabbitMq(configuration);

        services.AddHostedService<UserLoggedInConsumer>();
        services.AddHostedService<UserRegisteredConsumer>();
        services.AddHostedService<OrderCreatedConsumer>();
        services.AddHostedService<NotificationSendRequestedConsumer>();
        return services;
    }
}
