using System.Reflection;
using FluentValidation;
using Hdos.Common.Extensions;
using Hdos.LakehouseService.Application.EventHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.LakehouseService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddLakehouseApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddCommonMediatRBehaviors();

        services.AddScoped<LakehouseDataReadyHandler>();
        services.AddHttpClient("lakehouse");

        return services;
    }
}
