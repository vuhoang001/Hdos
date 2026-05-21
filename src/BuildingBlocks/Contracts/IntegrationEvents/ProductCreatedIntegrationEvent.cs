namespace Hdos.Contracts.IntegrationEvents;

public sealed record ProductCreatedIntegrationEvent(
    Guid    ProductId,
    string  ProductName,
    decimal ProductPrice,
    int     TotalProductCount,
    decimal TotalProductsPrice,
    decimal AverageProductPrice)
    : IntegrationEvent;
