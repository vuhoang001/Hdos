using Hdos.OrderService.Domain.Events;
using Hdos.OrderService.Domain.ValueObjects;
using Hdos.SharedKernel;

namespace Hdos.OrderService.Domain.Entities;

public enum OrderStatus
{
    Pending   = 0,
    Confirmed = 1,
    Cancelled = 2
}

public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderItem> _items = new();

    public Guid CustomerId { get; private set; }
    public string CustomerEmail { get; private set; } = default!;
    public OrderStatus Status { get; private set; }
    public Money Total { get; private set; } = default!;

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    private Order()
    {
    }

    public static Order Create(Guid customerId, string customerEmail,
        IEnumerable<(string product, int qty, decimal unit, string currency)> lines)
    {
        if (string.IsNullOrWhiteSpace(customerEmail))
            throw new ArgumentException("Customer email required", nameof(customerEmail));

        var order = new Order
        {
            Id            = Guid.NewGuid(),
            CustomerId    = customerId,
            CustomerEmail = customerEmail.Trim().ToLowerInvariant(),
            Status        = OrderStatus.Pending,
            Total         = Money.Zero()
        };

        var any = false;
        foreach (var line in lines)
        {
            order._items.Add(new OrderItem(order.Id, line.product, line.qty, Money.Of(line.unit, line.currency)));
            any = true;
        }

        if (!any) throw new InvalidOperationException("Order must contain at least one item.");

        order.Total = order._items.Aggregate(Money.Zero(order._items[0].UnitPrice.Currency),
                                             (acc, item) => acc.Add(item.LineTotal));

        var eventItems = order._items
            .Select(i => (i.ProductName, i.Quantity, i.UnitPrice.Amount))
            .ToList();

        order.RaiseDomainEvent(new OrderCreatedDomainEvent(
            order.Id, order.CustomerId, order.CustomerEmail, order.Total.Amount, eventItems));
        return order;
    }


    public void Confirm()
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Only pending orders can be confirmed.");
        Status       = OrderStatus.Confirmed;
        UpdatedAtUtc = DateTime.UtcNow;

        RaiseDomainEvent(new OrderConfirmedDomainEvent(Id, CustomerId, CustomerEmail, Status.ToString()));
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled) return;
        Status       = OrderStatus.Cancelled;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}