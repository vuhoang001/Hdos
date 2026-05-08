using FluentAssertions;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.OrderService.Application.Abstractions;
using Hdos.OrderService.Application.DTOs;
using Hdos.OrderService.Application.Features.CreateOrder;
using Hdos.OrderService.Domain.Entities;
using Hdos.OrderService.Domain.Repositories;
using Hdos.SharedKernel;
using NSubstitute;
using Xunit;

namespace Hdos.OrderService.Tests.Application;

public sealed class CreateOrderCommandHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IEventBus _bus = Substitute.For<IEventBus>();
    private readonly IUserLookupService _users = Substitute.For<IUserLookupService>();

    private CreateOrderCommandHandler NewHandler() => new(_orders, _uow, _bus, _users);

    private static CreateOrderCommand Command(Guid customerId, string? email = null) =>
        new(customerId, email, new[] { new OrderItemInputDto("Book", 2, 15.50m, "USD") });

    [Fact]
    public async Task Handle_UserVerified_PersistsAndPublishes()
    {
        var customerId = Guid.NewGuid();
        _users.GetByIdAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupDto(customerId, "alice@hdos.io", "Alice"));

        var result = await NewHandler().Handle(Command(customerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CustomerEmail.Should().Be("alice@hdos.io"); // taken from gRPC, not request

        await _orders.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<OrderCreatedIntegrationEvent>(e =>
                e.CustomerId == customerId &&
                e.CustomerEmail == "alice@hdos.io" &&
                e.TotalAmount == 31.00m &&
                e.Items.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserLookupFailure_ReturnsFailure_AndDoesNotPersist()
    {
        var customerId = Guid.NewGuid();
        _users.GetByIdAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UserLookupDto>(Error.NotFound("User")));

        var result = await NewHandler().Handle(Command(customerId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");

        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(
            Arg.Any<OrderCreatedIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UsesEmailFromAuth_NotFromRequest()
    {
        var customerId = Guid.NewGuid();
        _users.GetByIdAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupDto(customerId, "real@hdos.io", "Real"));

        var result = await NewHandler().Handle(
            Command(customerId, email: "spoofed@evil.io"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CustomerEmail.Should().Be("real@hdos.io");
    }
}
