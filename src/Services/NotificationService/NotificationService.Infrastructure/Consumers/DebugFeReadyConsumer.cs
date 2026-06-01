using Hdos.Common.Messaging;
using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

[ExternalConsumer("be.hdos.dashboard.fe.ready.debug")]
public sealed class DebugFeReadyConsumer(DebugFeReadyHandler handler)
    : IConsumer<DebugFeReadyMessage>
{
    public Task Consume(ConsumeContext<DebugFeReadyMessage> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
