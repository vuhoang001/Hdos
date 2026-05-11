using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Hdos.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;

namespace Hdos.Common.Messaging;

public sealed class RabbitMqEventBus : IEventBus
{
    private static readonly ActivitySource _activitySource = new("Hdos.Messaging");
    private static readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;

    private readonly RabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventBus> _logger;

    public RabbitMqEventBus(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventBus> logger)
    {
        _connection = connection;
        _options = options.Value;
        _logger = logger;
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IntegrationEvent
    {
        var routingKey = typeof(TEvent).Name;

        using var activity = _activitySource.StartActivity(
            $"rabbitmq publish {routingKey}",
            ActivityKind.Producer);

        using var channel = _connection.CreateChannel();
        channel.ExchangeDeclare(_options.Exchange, ExchangeType.Topic, durable: true);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event, @event.GetType()));

        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        props.MessageId = @event.EventId.ToString();
        props.Type = routingKey;
        props.ContentType = "application/json";
        props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        props.Headers = new Dictionary<string, object>();

        // Inject W3C traceparent/tracestate so consumers can continue the trace
        _propagator.Inject(
            new PropagationContext(activity?.Context ?? Activity.Current?.Context ?? default, Baggage.Current),
            props.Headers,
            static (headers, key, value) => headers[key] = Encoding.UTF8.GetBytes(value));

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", routingKey);
        activity?.SetTag("messaging.destination_kind", "topic");

        channel.BasicPublish(
            exchange: _options.Exchange,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: props,
            body: body);

        _logger.LogInformation("Published integration event {EventType} (Id={EventId})",
            routingKey, @event.EventId);
        return Task.CompletedTask;
    }
}
