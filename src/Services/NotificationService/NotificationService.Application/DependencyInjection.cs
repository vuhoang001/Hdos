using System.Reflection;
using Hdos.Common.Extensions;
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

        services.AddScoped<UserLoggedInEventHandler>();
        services.AddScoped<UserRegisteredEventHandler>();
        services.AddScoped<OrderCreatedEventHandler>();
        services.AddScoped<NotificationSendRequestedEventHandler>();
        services.AddScoped<ProductCreatedIntegrationEventHandler>();
        services.AddScoped<ProductTotalUpdatedHandler>();
        services.AddScoped<BaoCaoKhoaCreatedHandler>();
        services.AddScoped<DashboardFeReadyHandler>();
        services.AddScoped<HoanggggfEventHandler>();
        services.AddScoped<OrderConfirmedEventHandler>();
        services.AddScoped<TestIntegrationEventHandler>();

        return services;
    }
}