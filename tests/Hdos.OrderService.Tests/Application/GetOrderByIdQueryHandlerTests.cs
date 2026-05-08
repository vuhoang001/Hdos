using FluentAssertions;
using Hdos.OrderService.Application.Features.GetOrder;
using Hdos.OrderService.Domain.Entities;
using Hdos.OrderService.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace Hdos.OrderService.Tests.Application;

public sealed class GetOrderByIdQueryHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private GetOrderByIdQueryHandler NewHandler() => new(_orders);

    [Fact]
    public async Task Handle_Found_ReturnsDto()
    {
        var order = Order.Create(Guid.NewGuid(), "a@b.io",
            new[] { ("Book", 1, 10m, "USD") });
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var result = await NewHandler().Handle(new GetOrderByIdQuery(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(order.Id);
        result.Value.TotalAmount.Should().Be(10m);
        result.Value.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_Missing_ReturnsNotFound()
    {
        _orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Order?)null);

        var result = await NewHandler().Handle(new GetOrderByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }
}
