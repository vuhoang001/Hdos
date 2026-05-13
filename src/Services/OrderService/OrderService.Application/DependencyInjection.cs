using System.Reflection;
using FluentValidation;
using Hdos.Common.Extensions;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.OrderService.Application.EventHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.OrderService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOrderApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddCommonMediatRBehaviors();

        services.AddScoped<IIntegrationEventHandler<OrderCreateRequestedIntegrationEvent>, OrderCreateRequestedEventHandler>();
        services.AddScoped<OrderCreateRequestedEventHandler>();

        return services;
    }
}
