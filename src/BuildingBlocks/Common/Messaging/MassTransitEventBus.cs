using Hdos.Contracts.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Hdos.Common.Messaging;

public sealed class MassTransitEventBus(
    IPublishEndpoint publishEndpoint,
    ILogger<MassTransitEventBus> logger) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IntegrationEvent
    {
        await publishEndpoint.Publish(@event, @event.GetType(), ct);
        logger.LogInformation("Published {EventType} ({EventId})", @event.EventType, @event.EventId);
    }
}
