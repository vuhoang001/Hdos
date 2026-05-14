using System.Reflection;
using Hdos.Common.Extensions;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.NotificationService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddCommonMediatRBehaviors();

        services.AddScoped<IIntegrationEventHandler<UserLoggedInIntegrationEvent>, UserLoggedInEventHandler>();
        services.AddScoped<UserLoggedInEventHandler>();

        services.AddScoped<IIntegrationEventHandler<UserRegisteredIntegrationEvent>, UserRegisteredEventHandler>();
        services.AddScoped<UserRegisteredEventHandler>();

        services.AddScoped<IIntegrationEventHandler<OrderCreatedIntegrationEvent>, OrderCreatedEventHandler>();
        services.AddScoped<OrderCreatedEventHandler>();

        services.AddScoped<IIntegrationEventHandler<NotificationSendRequestedIntegrationEvent>, NotificationSendRequestedEventHandler>();
        services.AddScoped<NotificationSendRequestedEventHandler>();

        services.AddScoped<IIntegrationEventHandler<TestIntegrationEvent>, TestIntegrationEventHandler>();
        services.AddScoped<TestIntegrationEventHandler>();

        return services;
    }
}
