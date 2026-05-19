namespace Hdos.OrderService.Application.DTOs;

public sealed record OrderItemInputDto(string ProductName, int Quantity, decimal UnitPrice, string Currency);


public sealed record OrderItemDto(Guid Id, string ProductName, int Quantity, decimal UnitPrice, string Currency);

public sealed record OrderDto(
    Guid Id,
    Guid CustomerId,
    string CustomerEmail,
    string Status,
    decimal TotalAmount,
    string Currency,
    DateTime CreatedAtUtc,
    IReadOnlyList<OrderItemDto> Items);