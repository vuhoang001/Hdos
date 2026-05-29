using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class ProcessedToFeConsumer(ProcessedToFeHandler handler)
    : IConsumer<ProcessedToFeMessage>
{
    public Task Consume(ConsumeContext<ProcessedToFeMessage> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
