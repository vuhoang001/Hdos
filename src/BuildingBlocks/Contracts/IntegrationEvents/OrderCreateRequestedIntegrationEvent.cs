namespace Hdos.Contracts.IntegrationEvents;

public sealed record OrderCreateRequestedIntegrationEvent(
    Guid CorrelationId,
    Guid CustomerId,
    IReadOnlyList<OrderItemDto> Items) : IntegrationEvent;
