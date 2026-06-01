using System.Diagnostics;
using Hdos.Contracts.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hdos.Common.Messaging;

public sealed class MassTransitEventBus(
    IPublishEndpoint publishEndpoint,
    IHostEnvironment environment,
    ILogger<MassTransitEventBus> logger) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IntegrationEvent
    {
        // Auto-enrich theo CloudEvents standard nếu chưa được set
        var enriched = @event with
        {
            Source = string.IsNullOrEmpty(@event.Source)
                ? environment.ApplicationName
                : @event.Source,

            CorrelationId = string.IsNullOrEmpty(@event.CorrelationId)
                ? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
                : @event.CorrelationId
        };

        await publishEndpoint.Publish(enriched, enriched.GetType(), ct);

        logger.LogInformation(
            "Published {EventType} | id={EventId} source={Source} correlation={CorrelationId} v={Version}",
            enriched.EventType, enriched.EventId, enriched.Source,
            enriched.CorrelationId, enriched.Version);
    }
}
