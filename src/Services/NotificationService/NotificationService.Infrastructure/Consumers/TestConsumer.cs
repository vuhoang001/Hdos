using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class TestConsumer(TestIntegrationEventHandler handler)
    : IConsumer<TestIntegrationEvent>
{
    public Task Consume(ConsumeContext<TestIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
