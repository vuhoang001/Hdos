using System.Reflection;
using FluentValidation;
using Hdos.Common.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.AuthService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddCommonMediatRBehaviors();
        return services;
    }
}
