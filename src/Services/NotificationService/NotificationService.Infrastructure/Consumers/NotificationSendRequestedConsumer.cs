using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class NotificationSendRequestedConsumer(NotificationSendRequestedEventHandler handler)
    : IConsumer<NotificationSendRequestedIntegrationEvent>
{
    public Task Consume(ConsumeContext<NotificationSendRequestedIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
