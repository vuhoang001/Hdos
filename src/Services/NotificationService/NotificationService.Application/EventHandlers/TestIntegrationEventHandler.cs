using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;

namespace Hdos.NotificationService.Application.EventHandlers;

public class TestIntegrationEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
{
    public Task HandleAsync(TestIntegrationEvent @event, CancellationToken ct)
    {
        Console.WriteLine("Test integrations");
        return Task.CompletedTask;
    }
}