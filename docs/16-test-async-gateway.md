# 16 — Hướng dẫn test Async Gateway + xem RabbitMQ

## Khởi động stack

```bash
docker compose up -d --build
```

Chờ tất cả services healthy (khoảng 30-60s):
```bash
docker compose ps
# Tất cả phải ở trạng thái "running" hoặc "healthy"
```

---

## Bước 1 — Lấy JWT token

```bash
# Đăng ký tài khoản
curl -s -X POST http://localhost:5000/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"test@hdos.dev","password":"Test123!","fullName":"Test User"}' \
  | jq .

# Đăng nhập lấy token
TOKEN=$(curl -s -X POST http://localhost:5000/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test@hdos.dev","password":"Test123!"}' \
  | jq -r '.data.token')

echo $TOKEN
```

---

## Bước 2 — Mở RabbitMQ Management UI

Truy cập: **http://localhost:15672** (guest / guest)

Trước khi test, vào tab **Queues** để xem danh sách queue. Lần đầu chạy sẽ chưa có queue nào — chúng được tạo tự động khi consumer khởi động.

Nếu consumer đã start, bạn sẽ thấy các queue:
```
notification.order-created
notification.user-logged-in
notification.user-registered
notification.send-requested      ← queue mới (NotificationService consumer)
order.create-requested           ← queue mới (OrderService consumer)
```

---

## Bước 3 — Test Pattern 1: Sync REST

### 3.1 Tạo order (sync)

```bash
curl -s -X POST http://localhost:5000/orders/ \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "'$(curl -s http://localhost:5000/auth/me -H "Authorization: Bearer $TOKEN" | jq -r '.data.id')'",
    "items": [
      { "productName": "Thuoc A", "quantity": 2, "unitPrice": 50000, "currency": "VND" }
    ]
  }' | jq .
```

**Kết quả mong đợi:** `201 Created` với đầy đủ order data ngay trong response.

```json
{
  "success": true,
  "data": {
    "id": "...",
    "status": "Pending",
    "totalAmount": 100000,
    "items": [...]
  }
}
```

### 3.2 Xem trong RabbitMQ sau khi tạo order sync

Vào **http://localhost:15672 → Queues → notification.order-created**:
- `Messages ready`: momentarily tăng lên 1, rồi về 0 (đã được consume bởi NotificationService)
- Nếu muốn bắt được message trước khi consume: vào tab **Get messages** rồi gửi request ngay

---

## Bước 4 — Test Pattern 2: Async Queue

### 4.1 Xem Swagger của ApiGateway

Truy cập: **http://localhost:5000/async/swagger**

Bạn sẽ thấy 2 endpoints:
- `POST /async/orders`
- `POST /async/notifications/send`

### 4.2 Tạo order (async) qua curl

Lấy userId trước:
```bash
USER_ID=$(curl -s http://localhost:5000/auth/me \
  -H "Authorization: Bearer $TOKEN" | jq -r '.data.id')
```

Gửi async request:
```bash
RESPONSE=$(curl -s -X POST http://localhost:5000/async/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"customerId\": \"$USER_ID\",
    \"items\": [
      { \"productName\": \"Thuoc B async\", \"quantity\": 1, \"unitPrice\": 75000 }
    ]
  }")

echo $RESPONSE | jq .
CORRELATION_ID=$(echo $RESPONSE | jq -r '.data.correlationId')
echo "CorrelationId: $CORRELATION_ID"
```

**Kết quả mong đợi:** `202 Accepted` ngay lập tức:
```json
{
  "success": true,
  "data": {
    "correlationId": "f7e6d5c4-b3a2-1098-7654-321098765432",
    "status": "queued"
  }
}
```

### 4.3 Xem message trong RabbitMQ Management UI

**Ngay sau khi gửi request (trước khi consumer xử lý xong):**

1. Vào **http://localhost:15672 → Queues**
2. Click vào queue **`order.create-requested`**
3. Kéo xuống phần **Get messages** → nhấn **Get Message(s)**

Bạn sẽ thấy raw message JSON:
```json
{
  "CorrelationId": "f7e6d5c4-...",
  "CustomerId": "3fa85f64-...",
  "Items": [
    { "ProductName": "Thuoc B async", "Quantity": 1, "UnitPrice": 75000 }
  ],
  "EventId": "a2b3c4d5-...",
  "OccurredOnUtc": "2026-05-13T10:30:00Z",
  "EventType": "OrderCreateRequestedIntegrationEvent"
}
```

**AMQP Properties** (click vào message để xem chi tiết):
- `message_id`: EventId
- `type`: `OrderCreateRequestedIntegrationEvent`
- `content_type`: `application/json`
- `headers.traceparent`: W3C trace context (dùng cho Grafana Tempo)

### 4.4 Xem Exchange và routing

Vào **http://localhost:15672 → Exchanges → hdos.events**:
- Tab **Bindings**: thấy các queue bind vào exchange với routing key = tên event class
- Tab **Publish message**: có thể publish thủ công để test consumer

```
hdos.events (topic)
  ├── OrderCreateRequestedIntegrationEvent  → order.create-requested
  ├── NotificationSendRequestedIntegrationEvent → notification.send-requested
  ├── OrderCreatedIntegrationEvent          → notification.order-created
  ├── UserRegisteredIntegrationEvent        → notification.user-registered
  └── UserLoggedInIntegrationEvent          → notification.user-logged-in
```

### 4.5 Verify order đã được tạo sau khi consumer xử lý

```bash
# Đợi vài giây cho consumer xử lý
sleep 3

# Lấy danh sách orders
curl -s http://localhost:5000/orders/ \
  -H "Authorization: Bearer $TOKEN" | jq '.data[-1]'
# Phải thấy order "Thuoc B async" vừa tạo
```

---

## Bước 5 — Test async notification

```bash
curl -s -X POST http://localhost:5000/async/notifications/send \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "recipientEmail": "test@hdos.dev",
    "subject": "Test async notification",
    "body": "Day la notification gui qua async queue!"
  }' | jq .
```

Vào **RabbitMQ → Queues → notification.send-requested** để xem message tương tự.

Sau khi consumer xử lý, kiểm tra notification đã lưu:
```bash
curl -s http://localhost:5000/notifications/ \
  -H "Authorization: Bearer $TOKEN" | jq '.data[0]'
```

---

## Bước 6 — Xem logs service consumer

```bash
# OrderService — xem log khi consumer nhận message
docker logs hdos-orderservice-1 --tail 30 | grep -i "async\|correlat\|Requested"

# NotificationService — xem log khi consumer nhận message
docker logs hdos-notificationservice-1 --tail 30 | grep -i "async\|correlat\|Requested"

# ApiGateway — xem log khi publish
docker logs hdos-asyncgateway-1 --tail 20
```

Log mẫu từ OrderService consumer:
```
[10:30:01 INF] [OrderService] Processing async order creation. CorrelationId=f7e6d5c4 CustomerId=3fa85f64
[10:30:01 INF] [OrderService] Async order created. CorrelationId=f7e6d5c4 OrderId=a1b2c3d4
```

---

## Bước 7 — Trace trên Grafana Tempo (nếu bật monitoring)

```bash
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d
```

1. Mở **http://localhost:3030** (admin/admin)
2. Vào **Explore → Tempo**
3. Tìm theo `CorrelationId` hoặc TraceId từ log

Distributed trace sẽ hiện toàn bộ span:
```
POST /async/orders (ApiGateway)
  └── rabbitmq publish OrderCreateRequestedIntegrationEvent
        └── rabbitmq process OrderCreateRequestedIntegrationEvent (OrderService)
              └── CreateOrderCommand handler
                    ├── gRPC GetUserById → AuthService
                    └── rabbitmq publish OrderCreatedIntegrationEvent
                          └── rabbitmq process OrderCreatedIntegrationEvent (NotificationService)
```

---

## Checklist test nhanh

```
□ docker compose up -d --build
□ curl /auth/register + /auth/login → lấy TOKEN
□ Mở http://localhost:15672 → Queues → thấy order.create-requested
□ POST /async/orders → nhận 202 + correlationId
□ RabbitMQ → queue order.create-requested → Get messages → thấy message JSON
□ sleep 3 → curl /orders/ → thấy order mới
□ POST /async/notifications/send → nhận 202
□ RabbitMQ → queue notification.send-requested → Get messages
□ curl /notifications/ → thấy notification mới
□ docker logs hdos-orderservice-1 → thấy log "Async order created"
```
