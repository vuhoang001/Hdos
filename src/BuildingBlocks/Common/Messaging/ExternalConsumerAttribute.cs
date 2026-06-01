using MassTransit;

namespace Hdos.Common.Messaging;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ExternalConsumerAttribute(string queueName) : ExcludeFromConfigureEndpointsAttribute
{
    public string QueueName { get; } = queueName;

    /// <summary>Raw JSON deserializer — bật cho messages từ bên ngoài không dùng MassTransit envelope.</summary>
    public bool UseRawJson { get; init; } = true;

    public ushort PrefetchCount { get; init; } = 10;
    public int ConcurrentLimit { get; init; } = 5;
}
