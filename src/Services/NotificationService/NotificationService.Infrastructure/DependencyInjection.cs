using Hdos.Common.Extensions;
using Hdos.Common.Persistence;
using Hdos.NotificationService.Application.EventHandlers;
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
                                                                 sql => sql.MigrationsAssembly(
                                                                     typeof(NotificationDbContext).Assembly.FullName))
                                                             .AddInterceptors(
                                                                 sp.GetRequiredService<
                                                                     PublishDomainEventsInterceptor>()));

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddMassTransitMessaging(configuration, x =>
        {
            x.AddConsumer<UserLoggedInConsumer>();
            x.AddConsumer<UserRegisteredConsumer>();
            x.AddConsumer<OrderCreatedConsumer>();
            x.AddConsumer<OrderConfirmedConsumer>();
            x.AddConsumer<NotificationSendRequestedConsumer>();
            x.AddConsumer<ProductCreatedConsumer>();
            x.AddConsumer<ProductTotalUpdatedConsumer>();
            x.AddConsumer<HoanggggfConsumer>();
            x.AddConsumer<HoanggggfErrorConsumer>();
            x.AddConsumer<TestConsumer>();
        });

        return services;
    }
}