using FluentAssertions;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.OrderService.Application.EventHandlers;
using Hdos.OrderService.Domain.Events;
using NSubstitute;
using Xunit;

namespace Hdos.OrderService.Tests.Application;

public sealed class OrderCreatedIntegrationEventHandlerTests
{
    private readonly IEventBus _bus = Substitute.For<IEventBus>();

    [Fact]
    public async Task Handle_PublishesOrderCreatedIntegrationEvent_WithMappedItems()
    {
        var handler = new OrderCreatedIntegrationEventHandler(_bus);
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var domainEvent = new OrderCreatedDomainEvent(
            orderId, customerId, "alice@hdos.io", 31.00m,
            new[] { ("Book", 2, 15.50m) });

        await handler.Handle(domainEvent, CancellationToken.None);

        await _bus.Received(1).PublishAsync(
            Arg.Is<OrderCreatedIntegrationEvent>(e =>
                e.OrderId == orderId &&
                e.CustomerId == customerId &&
                e.CustomerEmail == "alice@hdos.io" &&
                e.TotalAmount == 31.00m &&
                e.Items.Count == 1 &&
                e.Items[0].ProductName == "Book"),
            Arg.Any<CancellationToken>());
    }
}
