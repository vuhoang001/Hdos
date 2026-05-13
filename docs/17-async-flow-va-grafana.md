# 17 — Luồng Async Gateway & Observability trên Grafana

Document này mô tả toàn bộ luồng kỹ thuật khi một request đi qua Async Gateway, và cách theo dõi từng bước trên Grafana (Loki + Tempo + Prometheus).

---

## 1. Toàn bộ luồng kỹ thuật

### 1.1 Ví dụ: `POST /async/notifications/send`

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ CLIENT                                                                       │
│   POST /async/notifications/send                                             │
│   Authorization: Bearer <JWT>                                                │
│   Body: { "recipientEmail": "user@hdos.dev", "subject": "...", "body": "..." }│
└────────────────────────────┬────────────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ NGINX (port 5000)                                                            │
│   location /async/ {                                                         │
│     auth_request /_auth_validate;   ← gọi AuthService validate JWT          │
│     proxy_pass http://asyncgateway;                                          │
│   }                                                                          │
└────────────────────────────┬────────────────────────────────────────────────┘
                             │ 202 nếu JWT hợp lệ
                             ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ ASYNCGATEWAY (AsyncNotificationsController.Send)                             │
│                                                                              │
│  1. [Authorize] kiểm tra JWT lần 2 (defense in depth)                       │
│  2. Tạo CorrelationId = Guid.NewGuid()                                       │
│  3. Tạo NotificationSendRequestedIntegrationEvent { CorrelationId, ... }    │
│  4. RabbitMqEventBus.PublishAsync():                                         │
│       - ExchangeDeclare("hdos.events", topic, durable)                       │
│       - Inject W3C traceparent vào AMQP headers                              │
│       - BasicPublish(routingKey: "NotificationSendRequestedIntegrationEvent")│
│  5. Return 202 Accepted { correlationId }                                    │
└────────────────────────────┬────────────────────────────────────────────────┘
                             │ Message trong RabbitMQ
                             ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ RABBITMQ                                                                     │
│   Exchange: hdos.events (topic)                                              │
│   Routing key: NotificationSendRequestedIntegrationEvent                     │
│   Queue: notification.send-requested (durable, ack manual)                  │
│   AMQP header: traceparent = "00-<traceId>-<spanId>-01"                     │
└────────────────────────────┬────────────────────────────────────────────────┘
                             │ consumer nhận message
                             ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ NOTIFICATIONSERVICE — NotificationSendRequestedConsumer (BackgroundService)  │
│                                                                              │
│  RabbitMqConsumerHostedService.OnMessageAsync():                             │
│  1. Extract W3C traceparent từ AMQP headers                                  │
│  2. StartActivity("rabbitmq process ...", parent=traceparent)                │
│       → cùng TraceId với AsyncGateway, là child span                        │
│  3. Push TraceId/SpanId vào ILogger scope (Serilog LogContext)              │
│  4. Gọi NotificationSendRequestedEventHandler.HandleAsync():                 │
│       a. LogInformation("Processing async notification...")  ← log có TraceId│
│       b. Notification.Create(recipient, subject, body)                       │
│       c. notification.MarkSent()                                             │
│       d. repo.AddAsync(notification) + uow.SaveChangesAsync()                │
│       e. pusher.PushToUserAsync(recipientEmail, notification.ToDto())        │
│  5. BasicAck → xóa message khỏi queue                                       │
└────────────────────────────┬────────────────────────────────────────────────┘
                             │ SignalR push
                             ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ SIGNALR HUB (NotificationHub /notifications/hubs/notifications)              │
│                                                                              │
│  EmailUserIdProvider.GetUserId() → lấy email từ JWT claim                   │
│  IHubContext.Clients.User(recipientEmail).SendAsync("notification", payload) │
│  → Client nhận event "notification" nếu đang kết nối hub với đúng email     │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 Luồng tương tự: `POST /async/orders`

Giống hệt nhưng routing key là `OrderCreateRequestedIntegrationEvent` → queue `order.create-requested` → OrderService consumer → MediatR pipeline `CreateOrderCommand` → AuthService gRPC validate → lưu DB.

---

## 2. Distributed Trace — W3C propagation qua AMQP

Đây là điểm quan trọng nhất: **một request client tạo ra MỘT TraceId duy nhất** chạy xuyên suốt AsyncGateway → RabbitMQ → Consumer.

```
TraceId: c29cd9125ad8fadf656669179fd769ff  (bất biến suốt hành trình)

Span 1 — [AsyncGateway]
  name: "POST /async/notifications/send"
  spanId: ec14c0b48257d3d8
  parent: (none — root)
  duration: ~3ms

Span 2 — [AsyncGateway]
  name: "rabbitmq publish NotificationSendRequestedIntegrationEvent"
  spanId: a2f3b1c4d5e6f708
  parent: ec14c0b48257d3d8   ← child của span 1
  duration: ~1ms
  attrs: messaging.system=rabbitmq, messaging.destination=NotificationSendRequestedIntegrationEvent

  ─── AMQP header: traceparent=00-c29cd912...-a2f3b1c4...-01 ───►

Span 3 — [NotificationService]
  name: "rabbitmq process NotificationSendRequestedIntegrationEvent"
  spanId: 9b8a7c6d5e4f3210
  parent: a2f3b1c4d5e6f708   ← child của span 2, cross-service!
  duration: ~5ms
  attrs: messaging.system=rabbitmq, messaging.rabbitmq.queue=notification.send-requested

    ├── Span 4 — EF Core INSERT (auto-instrumented)
    │     name: "INSERT Notifications"
    │     duration: ~4ms
    └── (SignalR push không tạo span riêng)
```

**Tại sao span 3 là child của span 2?**
`RabbitMqEventBus` inject W3C `traceparent` vào AMQP message headers. `RabbitMqConsumerHostedService` đọc header đó và dùng làm `parentContext` khi `StartActivity()`. OpenTelemetry SDK tự nối chúng thành một cây trace trong Tempo.

---

## 3. Quan sát trên Grafana — Hướng dẫn từng bước

> **Grafana URL:** `http://192.168.100.60:3030`
> **Login:** `admin` / `Hdos2024`

### Cách 1 — Bắt đầu từ Logs (Loki) → nhảy vào Trace (Tempo)

**Bước 1:** Vào **Explore** (icon la bàn ở sidebar trái) → chọn datasource **Loki**

**Bước 2:** Nhập query, bấm **Run query** (hoặc Shift+Enter):

```logql
{service_name="AsyncGateway"} |= "notifications/send" | json
```

Hoặc nếu muốn xem cả hai service cùng lúc (kết quả merge theo thời gian):

```logql
{service_name=~"AsyncGateway|NotificationService"} |= "notification" | json
```

**Bước 3:** Trong kết quả, mở rộng một log entry. Tìm field `TraceId` — bấm vào icon **"View Trace"** (mũi tên nhỏ bên cạnh giá trị TraceId)

→ Grafana tự động mở Tempo với trace đó.

**Bước 4:** Trong Tempo trace view:
- Thanh ngang = timeline (trục X là thời gian, độ rộng = duration)
- Màu khác nhau = service khác nhau
- Bấm vào từng span để xem attributes (routing key, queue name, HTTP status, ...)

---

### Cách 2 — Bắt đầu từ Trace (Tempo) → tìm theo điều kiện

**Bước 1:** Vào **Explore** → chọn datasource **Tempo**

**Bước 2:** Chọn tab **Search**, điền:

| Field | Giá trị |
|-------|---------|
| Service Name | `AsyncGateway` |
| Span Name | `rabbitmq publish NotificationSendRequestedIntegrationEvent` |
| Duration | `> 2ms` (tuỳ chọn) |

Bấm **Run query**

**Bước 3:** Bấm vào một TraceId trong kết quả → xem toàn bộ cây span

**Bước 4:** Trong span view, bấm **Logs for this span** → nhảy sang Loki và lọc logs theo TraceId đó tự động.

---

### Cách 3 — TraceQL (query mạnh nhất)

**Bước 1:** Vào **Explore** → **Tempo** → tab **TraceQL**

**Tìm tất cả notification traces trong 1 giờ qua:**
```
{ span.messaging.destination = "NotificationSendRequestedIntegrationEvent" }
```

**Tìm trace chậm hơn 10ms:**
```
{ span.messaging.destination = "NotificationSendRequestedIntegrationEvent" } | duration > 10ms
```

**Tìm trace của một recipient cụ thể (nếu có tag):**
```
{ resource.service.name = "NotificationService" && span.messaging.rabbitmq.queue = "notification.send-requested" }
```

---

### Cách 4 — Tìm theo CorrelationId (từ response 202)

Khi client gọi `POST /async/notifications/send`, response trả về:
```json
{ "data": { "correlationId": "dc31669c-8f35-4307-9c3f-b3abbe9e14ac" } }
```

Dùng `correlationId` này để tìm trong Loki:
```logql
{service_name="NotificationService"} |= "dc31669c-8f35-4307-9c3f-b3abbe9e14ac"
```

→ Thấy log `Processing async notification. CorrelationId=dc31669c...` → lấy TraceId → vào Tempo.

---

## 4. Checklist test nhanh end-to-end

```
□ 1. Login và lấy JWT token
      POST http://192.168.100.60:5000/auth/login
      Body: { "email": "user@hdos.dev", "password": "..." }
      → Lưu token

□ 2. Gửi notification async
      POST http://192.168.100.60:5000/async/notifications/send
      Authorization: Bearer <token>
      Body: {
        "recipientEmail": "<chính email của mình>",
        "subject": "Test trace",
        "body": "Hello from async flow"
      }
      → Lưu correlationId từ response

□ 3. Kiểm tra Loki — AsyncGateway
      {service_name="AsyncGateway"} |= "notifications/send"
      → Thấy log "Published integration event NotificationSendRequestedIntegrationEvent"
      → TraceId có giá trị hex (không phải N/A)

□ 4. Kiểm tra Loki — NotificationService
      {service_name="NotificationService"} |= "<correlationId>"
      → Thấy "Processing async notification. CorrelationId=..."
      → Thấy "Pushed SignalR notification ... → <email>"

□ 5. Nhảy vào Tempo từ TraceId
      → Thấy 3 span: HTTP POST / rabbitmq publish / rabbitmq process
      → Span 1 (AsyncGateway) là root
      → Span 3 (NotificationService) là leaf, cùng TraceId với span 1

□ 6. Kiểm tra SignalR client nhận event "notification"
      → Xem doc 14-signalr-realtime.md để kết nối hub
```

---

## 5. Cấu hình Observability cho AsyncGateway (đã apply)

Có 3 nơi đã được thêm để AsyncGateway xuất hiện trong monitoring:

### `docker-compose.monitoring.yml`
```yaml
asyncgateway:
  environment:
    OpenTelemetry__OtlpEndpoint: http://tempo:4317   # traces → Tempo
    Loki__Uri: http://loki:3100                       # logs → Loki
```

### `monitoring/prometheus.yml`
```yaml
- job_name: asyncgateway
  static_configs:
    - targets: [asyncgateway:8080]
      labels: { service: AsyncGateway }
  metrics_path: /metrics
```

### `RabbitMqConsumerHostedService.cs`
```csharp
// Inject TraceId/SpanId vào Serilog LogContext cho mọi background consumer
using var logScope = _logger.BeginScope(new Dictionary<string, object?>
{
    ["TraceId"] = activity?.TraceId.ToHexString(),
    ["SpanId"] = activity?.SpanId.ToHexString(),
});
await handler.HandleAsync(@event, CancellationToken.None);
```

Trước khi có fix này: consumer log hiện `TraceId=N/A` → không thể click "View Trace" từ Loki.
Sau fix: log consumer có `TraceId=<hex>` → click "View Trace" nhảy thẳng vào Tempo.

---

## 6. Lý do `recipientEmail` phải khớp với JWT email

`SignalRNotificationPusher` dùng:
```csharp
_hub.Clients.User(recipientEmail).SendAsync("notification", envelope);
```

`EmailUserIdProvider` map mỗi SignalR connection với email từ JWT claim. Nếu client kết nối hub với JWT có email `a@b.com` nhưng request body gửi `recipientEmail: "khac@b.com"` → SignalR tìm connection của `khac@b.com` → không thấy → **silent no-op**.

**Quy tắc:** `recipientEmail` trong request = email của client đang kết nối SignalR hub.

---

Xem thêm:
- [08 — Quan sát hệ thống](./08-quan-sat-he-thong.md)
- [09 — W3C Trace Context](./09-w3c-trace-context.md)
- [14 — SignalR Realtime](./14-signalr-realtime.md)
- [15 — Async Gateway](./15-async-gateway.md)
