using Hdos.SharedKernel;

namespace Hdos.OrderService.Domain.Events;

public sealed record ProductCreatedDomainEvent(Guid ProductId, string ProductName, decimal ProductPrice) : DomainEvent;