using Hdos.SharedKernel;

namespace Hdos.OrderService.Domain.Events;

public sealed record OrderCreatedDomainEvent(Guid OrderId, Guid CustomerId, decimal TotalAmount) : DomainEvent;
