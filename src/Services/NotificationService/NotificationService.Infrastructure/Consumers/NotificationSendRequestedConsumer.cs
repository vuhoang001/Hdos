using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class NotificationSendRequestedConsumer(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationSendRequestedConsumer> logger)
    : RabbitMqConsumerHostedService<NotificationSendRequestedIntegrationEvent, NotificationSendRequestedEventHandler>(
        connection, options, scopeFactory, logger,
        queueName: "notification.send-requested");
