using Hdos.Common.Behaviors;
using Hdos.Common.Messaging;
using Hdos.Common.Persistence;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.Common.Extensions;

public static class ServiceCollectionExtensions
{
    public const string HdosCorsPolicy = "HdosCors";

    public static IServiceCollection AddHdosCors(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(HdosCorsPolicy, policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });
        return services;
    }

    public static IServiceCollection AddRabbitMq(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IEventBus, RabbitMqEventBus>();
        return services;
    }

    public static IServiceCollection AddCommonMediatRBehaviors(this IServiceCollection services)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }

    /// <summary>
    /// Registers the EF Core interceptor that publishes domain events through
    /// MediatR after a successful SaveChanges, plus a generic logging handler
    /// that subscribes to every domain event type.
    ///
    /// Call from each service's Infrastructure DI, then attach the interceptor
    /// to its DbContext options:
    /// <code>
    /// services.AddDomainEventDispatching();
    /// services.AddDbContext&lt;TContext&gt;((sp, opts) =>
    ///     opts.UseSqlServer(...)
    ///         .AddInterceptors(sp.GetRequiredService&lt;PublishDomainEventsInterceptor&gt;()));
    /// </code>
    /// </summary>
    public static IServiceCollection AddDomainEventDispatching(this IServiceCollection services)
    {
        // Scoped: the interceptor depends on IPublisher (scoped) and runs inside
        // the same scope as the DbContext.
        services.AddScoped<PublishDomainEventsInterceptor>();
        services.AddTransient(typeof(INotificationHandler<>), typeof(LoggingDomainEventHandler<>));
        return services;
    }
}
