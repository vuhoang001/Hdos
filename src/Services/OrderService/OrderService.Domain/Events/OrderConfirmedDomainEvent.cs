using Hdos.SharedKernel;

namespace Hdos.OrderService.Domain.Events;

public sealed record OrderConfirmedDomainEvent(Guid OrderId, Guid CustomerId, string CustomerEmail, string Status) : DomainEvent;