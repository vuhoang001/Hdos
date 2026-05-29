using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class DashboardFeReadyConsumer(DashboardFeReadyHandler handler)
    : IConsumer<DashboardFeReadyIntegrationEvent>
{
    public Task Consume(ConsumeContext<DashboardFeReadyIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
