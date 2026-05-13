# 15 — Async Gateway (HTTP → Queue → Services)

Hdos hỗ trợ **hai cách tiếp nhận request** từ client:


| Luồng            | Path                                             | Phản hồi                    | Xử lý                           |
| ----------------- | ------------------------------------------------ | ----------------------------- | --------------------------------- |
| **Sync** (REST)   | `/orders/`, `/notifications/`, …                | Kết quả ngay (200/201/4xx)  | Service xử lý trong request     |
| **Async** (Queue) | `/async/orders`, `/async/notifications/send`, … | 202 Accepted +`CorrelationId` | Message queue → Service consumer |

---

## Kiến trúc

```
Client
  │
  ▼
Nginx (port 5000)
  │
  ├── /auth/         ──────────────────→ AuthService
  ├── /orders/       ──────────────────→ OrderService       (sync REST)
  ├── /notifications/──────────────────→ NotificationService(sync REST)
  ├── /m01/          ──────────────────→ M01Service
  │
  └── /async/        ──────────────────→ AsyncGateway
                                              │
                                        publish to RabbitMQ
                                        Exchange: hdos.events
                                              │
                              ┌───────────────┴────────────────┐
                              │                                 │
                  routing key:                        routing key:
            OrderCreateRequestedIntegrationEvent  NotificationSendRequestedIntegrationEvent
                              │                                 │
                              ▼                                 ▼
                     Queue: order.create-requested   Queue: notification.send-requested
                              │                                 │
                              ▼                                 ▼
                        OrderService                  NotificationService
                        (consumer)                    (consumer)
```

---

## AsyncGateway service

**Vị trí:** `src/Services/AsyncGateway/AsyncGateway.API/`

Đây là một **thin service** — không có database, không có domain logic. Nhiệm vụ duy nhất:

1. Nhận HTTP request (yêu cầu JWT hợp lệ)
2. Chuyển payload thành `IntegrationEvent`
3. Publish lên RabbitMQ exchange `hdos.events`
4. Trả về `202 Accepted` kèm `CorrelationId`

### Swagger UI

```
http://localhost:5000/async/swagger
```

### Endpoints

#### `POST /async/orders`

Tạo order bất đồng bộ. OrderService sẽ nhận và xử lý qua queue.

**Request body:**

```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "items": [
    {
      "productName": "Thuốc A",
      "quantity": 2,
      "unitPrice": 50000
    }
  ]
}
```

**Response `202 Accepted`:**

```json
{
  "success": true,
  "data": {
    "correlationId": "a1b2c3d4-...",
    "status": "queued"
  }
}
```

#### `POST /async/notifications/send`

Gửi notification bất đồng bộ. NotificationService sẽ lưu và push qua SignalR.

**Request body:**

```json
{
  "recipientEmail": "user@example.com",
  "subject": "Tiêu đề thông báo",
  "body": "Nội dung chi tiết..."
}
```

**Response `202 Accepted`:**

```json
{
  "success": true,
  "data": {
    "correlationId": "a1b2c3d4-...",
    "status": "queued"
  }
}
```

---

## Integration Events (command-style)

Các event mới được thêm vào `src/BuildingBlocks/Contracts/IntegrationEvents/`:

```csharp
// OrderCreateRequestedIntegrationEvent.cs
public sealed record OrderCreateRequestedIntegrationEvent(
    Guid CorrelationId,
    Guid CustomerId,
    IReadOnlyList<OrderItemDto> Items) : IntegrationEvent;

// NotificationSendRequestedIntegrationEvent.cs
public sealed record NotificationSendRequestedIntegrationEvent(
    Guid CorrelationId,
    string RecipientEmail,
    string Subject,
    string Body) : IntegrationEvent;
```

**Quy ước đặt tên:** Suffix `*RequestedIntegrationEvent` chỉ đây là **command-style event** (yêu cầu làm gì đó), khác với `*IntegrationEvent` thông thường là **domain event** (đã xảy ra).

---

## Consumer topology (RabbitMQ)

```
Exchange: hdos.events  (topic, durable)
│
├── OrderCreateRequestedIntegrationEvent
│        └── Queue: order.create-requested  → OrderService
│
└── NotificationSendRequestedIntegrationEvent
         └── Queue: notification.send-requested  → NotificationService
```

### Consumer trong OrderService

**File:** `OrderService.Infrastructure/Consumers/OrderCreateRequestedConsumer.cs`

Handler (`OrderCreateRequestedEventHandler`) nhận event, tái sử dụng pipeline MediatR `CreateOrderCommand` — bao gồm cả validation và gRPC call đến AuthService để verify user.

### Consumer trong NotificationService

**File:** `NotificationService.Infrastructure/Consumers/NotificationSendRequestedConsumer.cs`

Handler (`NotificationSendRequestedEventHandler`) tạo `Notification`, lưu DB, push qua SignalR đến recipient.

---

## Authentication

Tất cả endpoint của `/async/` đều yêu cầu JWT Bearer token (giống sync API):

- Nginx dùng `auth_request /_auth_validate` → AuthService
- AsyncGateway cũng có `[Authorize]` làm lớp thứ hai

---

## CorrelationId và Tracing

`CorrelationId` trong response cho phép:

- Tìm log trong Grafana/Loki: tìm theo field `CorrelationId`
- Trace distributed trên Grafana/Tempo: W3C traceparent được inject vào AMQP headers, consumer tiếp tục trace cùng span

---

## Thêm endpoint async mới

Để thêm một async endpoint mới (ví dụ: xử lý `CapCuu` bất đồng bộ):

**1. Thêm IntegrationEvent vào Contracts:**

```csharp
// src/BuildingBlocks/Contracts/IntegrationEvents/CapCuuReportedIntegrationEvent.cs
public sealed record CapCuuReportedIntegrationEvent(
    Guid CorrelationId,
    Guid BenhNhanId,
    string TriageLevel) : IntegrationEvent;
```

**2. Thêm Controller vào AsyncGateway:**

```csharp
// src/Services/AsyncGateway/AsyncGateway.API/Controllers/AsyncM01Controller.cs
[HttpPost("capcuu")]
public async Task<IActionResult> ReportCapCuu(
    [FromBody] ReportCapCuuAsyncRequest request, CancellationToken ct)
{
    var correlationId = Guid.NewGuid();
    await _eventBus.PublishAsync(new CapCuuReportedIntegrationEvent(
        correlationId, request.BenhNhanId, request.TriageLevel), ct);
    return Accepted(ApiResponse<AsyncResponse>.Ok(new AsyncResponse(correlationId)));
}
```

**3. Thêm EventHandler trong service consumer:**

```csharp
// M01Service.Application/EventHandlers/CapCuuReportedEventHandler.cs
public class CapCuuReportedEventHandler : IIntegrationEventHandler<CapCuuReportedIntegrationEvent>
{
    public async Task HandleAsync(CapCuuReportedIntegrationEvent @event, CancellationToken ct)
    {
        // business logic...
    }
}
```

**4. Thêm Consumer hosted service + đăng ký DI** (tương tự `NotificationSendRequestedConsumer`).

Không cần chỉnh nginx hay docker-compose vì `/async/` đã được route đến AsyncGateway.

---

## Khi nào dùng Sync vs Async?


| Dùng Sync (`/orders/`)                       | Dùng Async (`/async/orders`)                             |
| --------------------------------------------- | --------------------------------------------------------- |
| Cần kết quả ngay (VD: lấy order detail)   | Fire-and-forget (không cần chờ)                        |
| Cần validation trả về lỗi cho client ngay | Throughput cao, client chịu được eventual consistency |
| Mutation nhỏ, nhanh                          | Long-running processing                                   |
| UI cần feedback tức thì                    | Background jobs, batch processing                         |

---

Xem thêm: [07 — Giao tiếp nội bộ](./07-giao-tiep-noi-bo.md) | [09 — W3C Trace Context](./09-w3c-trace-context.md)
