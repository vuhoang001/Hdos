using FluentAssertions;
using Hdos.OrderService.Domain.Entities;
using Hdos.OrderService.Domain.Events;
using Xunit;

namespace Hdos.OrderService.Tests.Domain;

public sealed class OrderTests
{
    private static readonly Guid Customer = Guid.NewGuid();

    private static (string product, int qty, decimal unit, string currency) Line(string p, int q, decimal u, string c = "USD")
        => (p, q, u, c);

    [Fact]
    public void Create_LowercasesAndTrimsEmail_AndComputesTotal()
    {
        var order = Order.Create(
            Customer, "  ALICE@hdos.io  ",
            new[] { Line("Book", 2, 15.50m), Line("Pen", 3, 2m) });

        order.Id.Should().NotBe(Guid.Empty);
        order.CustomerId.Should().Be(Customer);
        order.CustomerEmail.Should().Be("alice@hdos.io");
        order.Status.Should().Be(OrderStatus.Pending);
        order.Total.Amount.Should().Be(2 * 15.50m + 3 * 2m);
        order.Total.Currency.Should().Be("USD");
        order.Items.Should().HaveCount(2);
    }

    [Fact]
    public void Create_RaisesOrderCreatedDomainEvent()
    {
        var order = Order.Create(Customer, "a@b.io", new[] { Line("Book", 1, 10m) });

        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderCreatedDomainEvent>()
            .Which.OrderId.Should().Be(order.Id);
    }

    [Fact]
    public void Create_NoItems_Throws()
    {
        var act = () => Order.Create(Customer, "a@b.io", Array.Empty<(string, int, decimal, string)>());
        act.Should().Throw<InvalidOperationException>().WithMessage("*at least one item*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankEmail_Throws(string email)
    {
        var act = () => Order.Create(Customer, email, new[] { Line("Book", 1, 1m) });
        act.Should().Throw<ArgumentException>().WithParameterName("customerEmail");
    }

    [Fact]
    public void Confirm_FromPending_Transitions()
    {
        var order = Order.Create(Customer, "a@b.io", new[] { Line("X", 1, 1m) });
        order.Confirm();

        order.Status.Should().Be(OrderStatus.Confirmed);
        order.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Confirm_NonPending_Throws()
    {
        var order = Order.Create(Customer, "a@b.io", new[] { Line("X", 1, 1m) });
        order.Cancel();

        var act = () => order.Confirm();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cancel_Idempotent()
    {
        var order = Order.Create(Customer, "a@b.io", new[] { Line("X", 1, 1m) });

        order.Cancel();
        order.Cancel(); // second call must not throw

        order.Status.Should().Be(OrderStatus.Cancelled);
    }
}
