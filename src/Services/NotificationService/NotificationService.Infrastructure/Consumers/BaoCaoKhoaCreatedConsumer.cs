using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class BaoCaoKhoaCreatedConsumer(BaoCaoKhoaCreatedHandler handler)
    : IConsumer<BaoCaoKhoaCreatedIntegrationEvent>
{
    public Task Consume(ConsumeContext<BaoCaoKhoaCreatedIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
