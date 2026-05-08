---
title: Thêm feature đầu tiên — "Cancel Order"
sidebar_position: 2
description: Đi qua trọn quy trình thêm 1 use case mới CQRS-style trong OrderService.
tags: [tutorial, cqrs, mediatr]
---

# Tutorial — Thêm feature "Cancel Order"

> **Loại:** Tutorial · **Thời lượng:** ~30 phút · **Pre-req:** Đã làm xong [Setup project](./setup-project)

Sau bài này bạn có 1 endpoint `POST /orders/{id}/cancel` hoạt động end-to-end, hiểu cách 4 layer (Domain / Application / Infrastructure / API) ráp lại.

## Bước 1 — Domain: thêm method `Cancel()` vào aggregate

```csharp:src/Services/OrderService/OrderService.Domain/Entities/Order.cs
public void Cancel(string reason)
{
    if (Status == OrderStatus.Cancelled)
        throw new DomainException("Order already cancelled");

    Status = OrderStatus.Cancelled;
    CancelledAtUtc = DateTime.UtcNow;
    RaiseDomainEvent(new OrderCancelledDomainEvent(Id, reason));
}
```

## Bước 2 — Application: tạo Command + Handler

Folder `Application/Features/CancelOrder/`:

```csharp:src/Services/OrderService/OrderService.Application/Features/CancelOrder/CancelOrderCommand.cs
public sealed record CancelOrderCommand(Guid OrderId, string Reason) : IRequest<Result>;

internal sealed class CancelOrderCommandHandler(
    IOrderRepository orders, IUnitOfWork uow, IEventBus bus)
    : IRequestHandler<CancelOrderCommand, Result>
{
    public async Task<Result> Handle(CancelOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(cmd.OrderId, ct);
        if (order is null) return Result.Failure(Error.NotFound("Order"));

        order.Cancel(cmd.Reason);
        orders.Update(order);
        await uow.SaveChangesAsync(ct);

        await bus.PublishAsync(new OrderCancelledIntegrationEvent(order.Id, cmd.Reason), ct);
        return Result.Success();
    }
}
```

## Bước 3 — API: thêm action

```csharp:src/Services/OrderService/OrderService.API/Controllers/OrdersController.cs
[HttpPost("{id:guid}/cancel")]
public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelOrderRequest req, CancellationToken ct)
{
    var result = await _sender.Send(new CancelOrderCommand(id, req.Reason), ct);
    return result.IsSuccess ? NoContent() : NotFound(result.Error);
}
```

## Bước 4 — Test bằng curl

```bash
ORDER_ID="..."
curl -X POST http://localhost:5000/orders/$ORDER_ID/cancel \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"reason":"Customer requested"}'
```

## Kế tiếp

- [Explanation: Domain vs Integration Events](../explanation/domain-vs-integration-events)
- [How-to: Tạo migration nếu thêm field mới](../how-to/create-migration)
