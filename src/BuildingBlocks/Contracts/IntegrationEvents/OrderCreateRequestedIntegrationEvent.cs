namespace Hdos.Contracts.IntegrationEvents;

public sealed record OrderCreateRequestedIntegrationEvent(
    Guid                        RequestId,
    Guid                        CustomerId,
    IReadOnlyList<OrderItemDto> Items) : IntegrationEvent;
