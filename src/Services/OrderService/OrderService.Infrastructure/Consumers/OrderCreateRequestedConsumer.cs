using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.OrderService.Application.EventHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hdos.OrderService.Infrastructure.Consumers;

public sealed class OrderCreateRequestedConsumer
    : RabbitMqConsumerHostedService<OrderCreateRequestedIntegrationEvent, OrderCreateRequestedEventHandler>
{
    public OrderCreateRequestedConsumer(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<OrderCreateRequestedConsumer> logger)
        : base(connection, options, scopeFactory, logger,
            queueName: "order.create-requested")
    { }
}
