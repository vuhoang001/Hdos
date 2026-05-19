# 15 — Async Gateway

Hdos hỗ trợ **hai cách tiếp nhận request** từ client:

| Luồng | Path | Phản hồi | Xử lý |
|-------|------|----------|-------|
| **Sync** (REST) | `/orders/`, `/notifications/`, … | Kết quả ngay (200/201/4xx) | Service xử lý trong request |
| **Async** (Queue) | `/async/orders`, `/async/notifications/send`, … | 202 Accepted + `CorrelationId` | Message queue → Service consumer |

---

## Mục lục

1. [Kiến trúc](#1-kiến-trúc)
2. [AsyncGateway service & Endpoints](#2-asyncgateway-service--endpoints)
3. [Integration Events](#3-integration-events)
4. [Consumer topology (RabbitMQ)](#4-consumer-topology-rabbitmq)
5. [Luồng kỹ thuật chi tiết](#5-luồng-kỹ-thuật-chi-tiết)
6. [Distributed Trace qua AMQP](#6-distributed-trace-qua-amqp)
7. [Test & Kiểm tra](#7-test--kiểm-tra)
8. [Quan sát trên Grafana](#8-quan-sát-trên-grafana)
9. [Thêm endpoint async mới](#9-thêm-endpoint-async-mới)
10. [Khi nào dùng Sync vs Async?](#10-khi-nào-dùng-sync-vs-async)

---

## 1. Kiến trúc

```
Client
  │
  ▼
Nginx (port 5000)
  │
  ├── /auth/         ──────────────────→ AuthService
  ├── /orders/       ──────────────────→ OrderService        (sync REST)
  ├── /notifications/──────────────────→ NotificationService (sync REST)
  ├── /m01/          ──────────────────→ M01Service
  │
  └── /async/        ──────────────────→ AsyncGateway
                                              │
                                        publish to RabbitMQ
                                        Exchange: hdos.events (topic)
                                              │
                              ┌───────────────┴────────────────┐
                    routing key:                      routing key:
            OrderCreateRequestedIntegrationEvent  NotificationSendRequestedIntegrationEvent
                              │                                 │
                              ▼                                 ▼
                     order.create-requested        notification.send-requested
                              │                                 │
                              ▼                                 ▼
                        OrderService                 NotificationService
                        (consumer)                   (consumer)
```

---

## 2. AsyncGateway service & Endpoints

**Vị trí:** `src/Services/AsyncGateway/AsyncGateway.API/`

Đây là một **thin service** — không có database, không có domain logic. Nhiệm vụ duy nhất:

1. Nhận HTTP request (yêu cầu JWT hợp lệ qua nginx `auth_request`)
2. Chuyển payload thành `IntegrationEvent`
3. Publish lên RabbitMQ exchange `hdos.events`
4. Trả về `202 Accepted` kèm `CorrelationId`

**Swagger:** `http://localhost:5000/async/swagger`

### `POST /async/orders`

```json
// Request
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "items": [
    { "productName": "Thuốc A", "quantity": 2, "unitPrice": 50000 }
  ]
}

// Response 202
{
  "success": true,
  "data": { "correlationId": "a1b2c3d4-...", "status": "queued" }
}
```

OrderService consumer nhận event, tái sử dụng pipeline MediatR `CreateOrderCommand` (có validation + gRPC call AuthService).

### `POST /async/notifications/send`

```json
// Request
{
  "recipientEmail": "user@example.com",
  "subject": "Tiêu đề thông báo",
  "body": "Nội dung chi tiết..."
}

// Response 202
{
  "success": true,
  "data": { "correlationId": "a1b2c3d4-...", "status": "queued" }
}
```

NotificationService consumer nhận event, tạo `Notification`, lưu DB, push qua SignalR.

> **Lưu ý:** `recipientEmail` phải khớp với email của client đang kết nối SignalR hub, vì `SignalRNotificationPusher` dùng `_hub.Clients.User(recipientEmail)` — nếu không khớp, push là silent no-op.

---

## 3. Integration Events

Đặt tại `src/BuildingBlocks/Contracts/IntegrationEvents/`:

```csharp
// Command-style events (suffix *RequestedIntegrationEvent)
public sealed record OrderCreateRequestedIntegrationEvent(
    Guid CorrelationId, Guid CustomerId,
    IReadOnlyList<OrderItemDto> Items) : IntegrationEvent;

public sealed record NotificationSendRequestedIntegrationEvent(
    Guid CorrelationId, string RecipientEmail,
    string Subject, string Body) : IntegrationEvent;
```

**Quy ước:** Suffix `*RequestedIntegrationEvent` = **command-style** (yêu cầu làm gì đó).  
`*IntegrationEvent` không có suffix = **domain event** (đã xảy ra).

---

## 4. Consumer topology (RabbitMQ)

```
Exchange: hdos.events (topic, durable)
│
├── OrderCreateRequestedIntegrationEvent   → order.create-requested   → OrderService
├── NotificationSendRequestedIntegrationEvent → notification.send-requested → NotificationService
├── OrderCreatedIntegrationEvent           → notification.order-created   → NotificationService
├── UserRegisteredIntegrationEvent         → notification.user-registered  → NotificationService
└── UserLoggedInIntegrationEvent           → notification.user-logged-in   → NotificationService
```

---

## 5. Luồng kỹ thuật chi tiết

Ví dụ `POST /async/notifications/send`:

```
CLIENT
  POST /async/notifications/send
  Authorization: Bearer <JWT>
  Body: { "recipientEmail": "...", "subject": "...", "body": "..." }
       │
       ▼
NGINX
  auth_request /_auth_validate → AuthService validate JWT
  proxy_pass http://asyncgateway
       │
       ▼
ASYNCGATEWAY (AsyncNotificationsController.Send)
  1. [Authorize] validate JWT (defense in depth)
  2. CorrelationId = Guid.NewGuid()
  3. Tạo NotificationSendRequestedIntegrationEvent { CorrelationId, ... }
  4. RabbitMqEventBus.PublishAsync():
       - ExchangeDeclare("hdos.events", topic, durable)
       - Inject W3C traceparent vào AMQP headers
       - BasicPublish(routingKey: "NotificationSendRequestedIntegrationEvent")
  5. Return 202 Accepted { correlationId }
       │
       ▼ (message trong RabbitMQ)
NOTIFICATIONSERVICE — NotificationSendRequestedConsumer (BackgroundService)
  1. Extract W3C traceparent từ AMQP headers
  2. StartActivity("rabbitmq process ...", parent=traceparent)
     → cùng TraceId với AsyncGateway, là child span
  3. Push TraceId/SpanId vào ILogger scope
  4. NotificationSendRequestedEventHandler.HandleAsync():
       a. Notification.Create(recipient, subject, body)
       b. repo.AddAsync + uow.SaveChangesAsync()
       c. pusher.PushToUserAsync(recipientEmail, notification.ToDto())
  5. BasicAck → xóa message khỏi queue
       │
       ▼ (SignalR push)
SIGNALR HUB (/notifications/hubs/notifications)
  EmailUserIdProvider → User(recipientEmail).SendAsync("notification", payload)
```

---

## 6. Distributed Trace qua AMQP

Một request tạo ra **một TraceId duy nhất** xuyên suốt AsyncGateway → RabbitMQ → Consumer:

```
TraceId: c29cd9125ad8fadf656669179fd769ff

Span 1 [AsyncGateway] POST /async/notifications/send
  spanId: ec14c0b48257d3d8  parent: none (root)

Span 2 [AsyncGateway] rabbitmq publish NotificationSendRequestedIntegrationEvent
  spanId: a2f3b1c4d5e6f708  parent: ec14c0b48257d3d8
  attrs: messaging.system=rabbitmq, messaging.destination=NotificationSendRequested...

  ──── AMQP header traceparent: 00-c29cd912...-a2f3b1c4...-01 ────►

Span 3 [NotificationService] rabbitmq process NotificationSendRequestedIntegrationEvent
  spanId: 9b8a7c6d5e4f3210  parent: a2f3b1c4d5e6f708 (cross-service!)
  attrs: messaging.rabbitmq.queue=notification.send-requested

  └── Span 4 [NotificationService] INSERT Notifications (EF Core auto-instrumented)
```

---

## 7. Test & Kiểm tra

### Khởi động

```bash
docker compose up -d --build
docker compose ps  # tất cả phải "running" hoặc "healthy"
```

### Bước 1 — Lấy JWT token

```bash
TOKEN=$(curl -sk https://localhost:8443/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@hdos.dev","password":"Admin1234!"}' \
  | jq -r '.data.token')
```

### Bước 2 — Mở RabbitMQ Management UI

**http://localhost:15672** (guest / guest)

Tab **Queues** — danh sách queues được tạo tự động khi consumer start:
```
notification.order-created
notification.user-registered
notification.send-requested        ← NotificationService consumer
order.create-requested             ← OrderService consumer
```

### Bước 3 — Test async order

```bash
USER_ID=$(curl -s http://localhost:5000/auth/me \
  -H "Authorization: Bearer $TOKEN" | jq -r '.data.id')

RESPONSE=$(curl -s -X POST http://localhost:5000/async/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"customerId\": \"$USER_ID\",
    \"items\": [{ \"productName\": \"Thuoc async\", \"quantity\": 1, \"unitPrice\": 75000 }]
  }")

echo $RESPONSE | jq .
CORRELATION_ID=$(echo $RESPONSE | jq -r '.data.correlationId')
echo "CorrelationId: $CORRELATION_ID"
```

**Kết quả mong đợi:** `202 Accepted` ngay lập tức.

**Xem message trong RabbitMQ:**
1. **http://localhost:15672 → Queues → order.create-requested**
2. Phần **Get messages** → **Get Message(s)**

Message raw:
```json
{
  "CorrelationId": "f7e6d5c4-...", "CustomerId": "3fa85f64-...",
  "Items": [{ "ProductName": "Thuoc async", "Quantity": 1, "UnitPrice": 75000 }],
  "EventType": "OrderCreateRequestedIntegrationEvent"
}
```

AMQP Properties: `headers.traceparent` = W3C trace context.

**Verify order đã được tạo:**
```bash
sleep 3
curl -s http://localhost:5000/orders/ -H "Authorization: Bearer $TOKEN" | jq '.data[-1]'
```

### Bước 4 — Test async notification

```bash
RESPONSE=$(curl -s -X POST http://localhost:5000/async/notifications/send \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "recipientEmail": "admin@hdos.dev",
    "subject": "Test async notification",
    "body": "Hello from async flow!"
  }')
echo $RESPONSE | jq .

# Verify
sleep 3
curl -s http://localhost:5000/notifications/ -H "Authorization: Bearer $TOKEN" | jq '.data[0]'
```

### Bước 5 — Xem Exchange bindings

**http://localhost:15672 → Exchanges → hdos.events → tab Bindings**

### Bước 6 — Xem logs consumer

```bash
# OrderService consumer log
docker logs hdos-orderservice-1 --tail 30 | grep -i "async\|correlat\|Requested"

# NotificationService consumer log
docker logs hdos-notificationservice-1 --tail 30 | grep -i "async\|correlat\|Requested"
```

Log mẫu:
```
[INF] Processing async order creation. CorrelationId=f7e6d5c4 CustomerId=3fa85f64
[INF] Async order created. CorrelationId=f7e6d5c4 OrderId=a1b2c3d4
```

### Checklist test nhanh

```
□ docker compose up -d --build — tất cả healthy
□ Lấy JWT token
□ Mở http://localhost:15672 → Queues → thấy order.create-requested
□ POST /async/orders → nhận 202 + correlationId
□ RabbitMQ → order.create-requested → Get messages → thấy message JSON
□ sleep 3 → curl /orders/ → thấy order mới
□ POST /async/notifications/send → nhận 202
□ sleep 3 → curl /notifications/ → thấy notification mới
□ docker logs hdos-orderservice-1 → thấy "Async order created"
```

---

## 8. Quan sát trên Grafana

> URL server: `http://192.168.100.60:3030` — Login: `admin / admin`

```bash
# Bật monitoring
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d
```

### Cách 1 — Từ Logs → Trace

1. **Explore → Loki** → nhập query:
```logql
{service_name="AsyncGateway"} |= "notifications/send" | json
```

2. Mở log entry → click **View Trace** bên cạnh giá trị TraceId → mở Tempo tự động.

3. Trong Tempo trace view:
   - Thanh ngang = timeline
   - Màu khác nhau = service khác nhau
   - Click span → xem attributes (routing key, queue name, status, ...)

### Cách 2 — Từ CorrelationId

Client nhận `correlationId` từ response 202. Tìm trong Loki:
```logql
{service_name="NotificationService"} |= "dc31669c-8f35-4307-9c3f-b3abbe9e14ac"
```

→ Thấy `Processing async notification. CorrelationId=dc31669c...` → lấy TraceId → vào Tempo.

### Cách 3 — TraceQL

**Explore → Tempo → tab TraceQL**:

```
# Tìm tất cả notification traces
{ span.messaging.destination = "NotificationSendRequestedIntegrationEvent" }

# Tìm trace chậm
{ span.messaging.destination = "NotificationSendRequestedIntegrationEvent" } | duration > 10ms

# Tìm theo queue
{ resource.service.name = "NotificationService"
  && span.messaging.rabbitmq.queue = "notification.send-requested" }
```

### Cách 4 — Search theo Service/Span

**Explore → Tempo → tab Search**:

| Field | Giá trị |
|-------|---------|
| Service Name | `AsyncGateway` |
| Span Name | `rabbitmq publish NotificationSendRequestedIntegrationEvent` |

→ Bấm vào TraceId → xem cây span đầy đủ → **Logs for this span** → nhảy sang Loki.

### Trace đầy đủ trông như thế nào

```
POST /async/orders [AsyncGateway] 3ms
  └── rabbitmq publish OrderCreateRequestedIntegrationEvent [AsyncGateway] 1ms
        └── rabbitmq process OrderCreateRequestedIntegrationEvent [OrderService] 45ms
              ├── gRPC GetUserById → AuthService 5ms
              └── rabbitmq publish OrderCreatedIntegrationEvent [OrderService] 1ms
                    └── rabbitmq process OrderCreatedIntegrationEvent [NotificationService] 8ms
```

---

## 9. Thêm endpoint async mới

Ví dụ: thêm async endpoint `POST /async/capcuu`.

**1. Thêm IntegrationEvent vào Contracts:**
```csharp
// src/BuildingBlocks/Contracts/IntegrationEvents/CapCuuReportedIntegrationEvent.cs
public sealed record CapCuuReportedIntegrationEvent(
    Guid CorrelationId, Guid BenhNhanId, string TriageLevel) : IntegrationEvent;
```

**2. Thêm Controller vào AsyncGateway:**
```csharp
// src/Services/AsyncGateway/AsyncGateway.API/Controllers/AsyncM01Controller.cs
[HttpPost("capcuu")]
public async Task<IActionResult> ReportCapCuu(
    [FromBody] ReportCapCuuAsyncRequest request, CancellationToken ct)
{
    var correlationId = Guid.NewGuid();
    await _eventBus.PublishAsync(
        new CapCuuReportedIntegrationEvent(correlationId, request.BenhNhanId, request.TriageLevel), ct);
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
        // business logic
    }
}
```

**4. Đăng ký DI trong M01Service:**
```csharp
// Application/DependencyInjection.cs
services.AddScoped<IIntegrationEventHandler<CapCuuReportedIntegrationEvent>, CapCuuReportedEventHandler>();
services.AddScoped<CapCuuReportedEventHandler>();

// Infrastructure/DependencyInjection.cs
services.AddHostedService<CapCuuReportedConsumer>();
```

Không cần chỉnh nginx hay docker-compose — `/async/` đã route đến AsyncGateway.

---

## 10. Khi nào dùng Sync vs Async?

| Dùng Sync (`/orders/`) | Dùng Async (`/async/orders`) |
|------------------------|------------------------------|
| Cần kết quả ngay | Fire-and-forget |
| Cần validation trả về lỗi ngay | Throughput cao, chấp nhận eventual consistency |
| Mutation nhỏ, nhanh | Long-running processing |
| UI cần feedback tức thì | Background jobs, batch processing |
