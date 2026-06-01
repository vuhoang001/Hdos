using System.Reflection;
using MassTransit;

namespace Hdos.Common.Messaging;

public static class ExternalConsumerExtensions
{
    /// <summary>
    /// Đăng ký tất cả class có [ExternalConsumer] trong assembly vào MassTransit DI.
    /// Gọi bên trong Action&lt;IBusRegistrationConfigurator&gt; của AddMassTransitMessaging.
    /// </summary>
    public static void AddExternalConsumers(
        this IBusRegistrationConfigurator x,
        Assembly assembly)
    {
        foreach (var type in GetExternalConsumerTypes(assembly))
            x.AddConsumer(type);
    }

    /// <summary>
    /// Tạo receive endpoint riêng cho mỗi [ExternalConsumer], áp dụng raw JSON nếu được khai báo.
    /// Gọi bên trong UsingRabbitMq, trước cfg.ConfigureEndpoints(ctx).
    /// </summary>
    public static void ConfigureExternalEndpoints(
        this IRabbitMqBusFactoryConfigurator cfg,
        IBusRegistrationContext ctx,
        Assembly assembly)
    {
        foreach (var type in GetExternalConsumerTypes(assembly))
        {
            var attr = type.GetCustomAttribute<ExternalConsumerAttribute>()!;

            cfg.ReceiveEndpoint(attr.QueueName, e =>
            {
                e.PrefetchCount = attr.PrefetchCount;
                e.ConcurrentMessageLimit = attr.ConcurrentLimit;

                if (attr.UseRawJson)
                    e.UseRawJsonDeserializer();

                e.ConfigureConsumer(ctx, type);
            });
        }
    }

    private static IEnumerable<Type> GetExternalConsumerTypes(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<ExternalConsumerAttribute>() is not null
                     && !t.IsAbstract
                     && !t.IsInterface);
}
