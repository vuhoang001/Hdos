namespace Hdos.Contracts.IntegrationEvents;

public sealed record ProductCreatedIntegrationEvent(
    Guid ProductId,
    string ProductName,
    decimal ProductPrice,
    decimal TotalProductsPrice)
    : IntegrationEvent;