using Hdos.Contracts.IntegrationEvents;
using Hdos.LakehouseService.Application.EventHandlers;
using MassTransit;

namespace Hdos.LakehouseService.Infrastructure.Consumers;

public sealed class LakehouseDataReadyConsumer(LakehouseDataReadyHandler handler)
    : IConsumer<LakehouseDataReadyIntegrationEvent>
{
    public Task Consume(ConsumeContext<LakehouseDataReadyIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
