using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public class TestConsumer : RabbitMqConsumerHostedService<TestIntegrationEvent, TestIntegrationEventHandler>
{
    public TestConsumer(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<UserLoggedInConsumer> logger)
        : base(connection, options, scopeFactory, logger,
               queueName: "haibigdhoangsmalld")
    {
    }
}