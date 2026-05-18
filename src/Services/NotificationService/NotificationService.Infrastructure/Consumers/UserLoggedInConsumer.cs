using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class UserLoggedInConsumer(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<UserLoggedInConsumer> logger)
    : RabbitMqConsumerHostedService<UserLoggedInIntegrationEvent, UserLoggedInEventHandler>(
        connection, options, scopeFactory, logger,
        queueName: "notification.user-logged-in");

public sealed class UserRegisteredConsumer(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<UserRegisteredConsumer> logger)
    : RabbitMqConsumerHostedService<UserRegisteredIntegrationEvent, UserRegisteredEventHandler>(
        connection, options, scopeFactory, logger,
        queueName: "notification.user-registered");

public sealed class OrderCreatedConsumer(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderCreatedConsumer> logger)
    : RabbitMqConsumerHostedService<OrderCreatedIntegrationEvent, OrderCreatedEventHandler>(
        connection, options, scopeFactory, logger,
        queueName: "notification.order-created");
