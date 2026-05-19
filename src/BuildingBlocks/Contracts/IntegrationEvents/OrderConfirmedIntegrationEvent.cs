namespace Hdos.Contracts.IntegrationEvents;

public sealed record OrderConfirmedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    string CustomerEmail,
    string OrderStatus
) : IntegrationEvent;