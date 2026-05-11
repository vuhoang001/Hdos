using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Hdos.Contracts.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Hdos.Common.Messaging;

public abstract class RabbitMqConsumerHostedService<TEvent, THandler> : BackgroundService
    where TEvent : IntegrationEvent
    where THandler : IIntegrationEventHandler<TEvent>
{
    private static readonly ActivitySource _activitySource = new("Hdos.Messaging");
    private static readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;

    private readonly RabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private readonly string _queueName;
    private IModel? _channel;

    protected RabbitMqConsumerHostedService(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        string queueName)
    {
        _connection = connection;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _queueName = queueName;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = _connection.CreateChannel();
        var routingKey = typeof(TEvent).Name;

        _channel.ExchangeDeclare(_options.Exchange, ExchangeType.Topic, durable: true);
        _channel.QueueDeclare(_queueName, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(_queueName, _options.Exchange, routingKey);
        _channel.BasicQos(0, 10, false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnMessageAsync;

        _channel.BasicConsume(_queueName, autoAck: false, consumer);
        _logger.LogInformation("Subscribed {Queue} -> {RoutingKey}", _queueName, routingKey);
        return Task.CompletedTask;
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs ea)
    {
        // Extract W3C traceparent from AMQP headers to continue the distributed trace
        var parentContext = _propagator.Extract(
            default,
            ea.BasicProperties.Headers,
            static (headers, key) =>
            {
                if (headers is not null &&
                    headers.TryGetValue(key, out var value) &&
                    value is byte[] bytes)
                    return [Encoding.UTF8.GetString(bytes)];
                return [];
            });

        Baggage.Current = parentContext.Baggage;

        using var activity = _activitySource.StartActivity(
            $"rabbitmq process {ea.RoutingKey}",
            ActivityKind.Consumer,
            parentContext.ActivityContext);

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", ea.RoutingKey);
        activity?.SetTag("messaging.rabbitmq.queue", _queueName);

        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
        try
        {
            var @event = JsonSerializer.Deserialize<TEvent>(json);
            if (@event is null) throw new InvalidOperationException("Null event payload");

            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<THandler>();
            await handler.HandleAsync(@event, CancellationToken.None);

            _channel!.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to handle event {RoutingKey}", ea.RoutingKey);
            _channel!.BasicNack(ea.DeliveryTag, multiple: false, requeue: !ea.Redelivered);
        }
    }

    public override void Dispose()
    {
        _channel?.Close();
        _channel?.Dispose();
        base.Dispose();
    }
}
