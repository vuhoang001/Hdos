namespace Hdos.OrderService.Application.DTOs;

public sealed record ProductDto(Guid Id, Guid ProductId, string ProductName, decimal ProductPrice);
