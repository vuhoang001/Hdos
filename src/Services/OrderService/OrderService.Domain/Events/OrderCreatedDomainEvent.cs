using Hdos.SharedKernel;

namespace Hdos.OrderService.Domain.Events;

public sealed record OrderCreatedDomainEvent(
    Guid                                                             OrderId,
    Guid                                                             CustomerId,
    string                                                           CustomerEmail,
    decimal                                                          TotalAmount,
    IReadOnlyList<(string ProductName, int Quantity, decimal UnitPrice)> Items
) : DomainEvent;
