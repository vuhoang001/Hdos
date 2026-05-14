using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;

namespace Hdos.NotificationService.Application.EventHandlers;

public class TestIntegrationEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
{
    public Task HandleAsync(TestIntegrationEvent @event, CancellationToken ct)
    {
        Console.WriteLine("Bo hoang vi dai 1102");
        return Task.CompletedTask;
    }
}