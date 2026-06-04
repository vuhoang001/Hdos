using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using MassTransit;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class UserLoggedInConsumer(UserLoggedInEventHandler handler)
    : IConsumer<UserLoggedInIntegrationEvent>
{
    public Task Consume(ConsumeContext<UserLoggedInIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}

public sealed class UserRegisteredConsumer(UserRegisteredEventHandler handler)
    : IConsumer<UserRegisteredIntegrationEvent>
{
    public Task Consume(ConsumeContext<UserRegisteredIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}

