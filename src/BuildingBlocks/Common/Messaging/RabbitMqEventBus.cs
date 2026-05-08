using System.Text;
using System.Text.Json;
using Hdos.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Hdos.Common.Messaging;

public sealed class RabbitMqEventBus : IEventBus
{
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
        using var channel = _connection.CreateChannel();
        channel.ExchangeDeclare(_options.Exchange, ExchangeType.Topic, durable: true);

        var routingKey = typeof(TEvent).Name;
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event, @event.GetType()));

        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        props.MessageId = @event.EventId.ToString();
        props.Type = routingKey;
        props.ContentType = "application/json";
        props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

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
