using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class NotificationSendRequestedConsumer
    : RabbitMqConsumerHostedService<NotificationSendRequestedIntegrationEvent, NotificationSendRequestedEventHandler>
{
    public NotificationSendRequestedConsumer(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationSendRequestedConsumer> logger)
        : base(connection, options, scopeFactory, logger,
            queueName: "notification.send-requested")
    { }
}
