using Hdos.Common.Behaviors;
using Hdos.Common.Messaging;
using Hdos.Common.Persistence;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.Common.Extensions;

public static class ServiceCollectionExtensions
{
    public const string HdosCorsPolicy = "HdosCors";

    public static IServiceCollection AddHdosCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var origins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy(HdosCorsPolicy, policy =>
            {
                // AllowAnyOrigin() không dùng được cùng AllowCredentials()
                // (trình duyệt chặn khi credentials:include + wildcard origin).
                // Luôn dùng WithOrigins() cụ thể + AllowCredentials().
                policy.WithOrigins(origins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });
        return services;
    }

    public static IServiceCollection AddMassTransitMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configure = null,
        string servicePrefix = "",
        Action<IRabbitMqBusFactoryConfigurator, IBusRegistrationContext>? configureReceiveEndpoints = null)
    {
        var options = configuration
            .GetSection(RabbitMqOptions.SectionName)
            .Get<RabbitMqOptions>() ?? new();

        services.AddMassTransit(x =>
        {
            if (string.IsNullOrEmpty(servicePrefix))
                x.SetKebabCaseEndpointNameFormatter();
            else
                x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(servicePrefix, false));

            configure?.Invoke(x);

            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.MessageTopology.SetEntityNameFormatter(new KebabCaseEntityNameFormatter());

                // Port phải encode vào URI — MassTransit không có overload riêng cho port
                var vhost = options.VirtualHost.TrimStart('/');
                var hostUri = new Uri($"amqp://{options.Host}:{options.Port}/{vhost}");
                cfg.Host(hostUri, h =>
                {
                    h.Username(options.UserName);
                    h.Password(options.Password);
                });

                cfg.UseMessageRetry(r => r.Exponential(
                                        retryLimit: 5,
                                        minInterval: TimeSpan.FromSeconds(1),
                                        maxInterval: TimeSpan.FromSeconds(30),
                                        intervalDelta: TimeSpan.FromSeconds(5)));

                // Custom endpoints trước ConfigureEndpoints để tránh duplicate
                configureReceiveEndpoints?.Invoke(cfg, ctx);

                cfg.ConfigureEndpoints(ctx);
            });
        });

        services.AddScoped<IEventBus, MassTransitEventBus>();
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