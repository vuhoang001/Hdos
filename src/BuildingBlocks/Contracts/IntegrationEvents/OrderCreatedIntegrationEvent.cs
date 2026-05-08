namespace Hdos.Contracts.IntegrationEvents;

public sealed record OrderCreatedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    string CustomerEmail,
    decimal TotalAmount,
    IReadOnlyList<OrderItemDto> Items) : IntegrationEvent;

public sealed record OrderItemDto(
    string ProductName,
    int Quantity,
    decimal UnitPrice);
