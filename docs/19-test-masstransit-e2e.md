# 19 — Test End-to-End: Publish → Consumer

Hướng dẫn chạy và kiểm chứng toàn bộ luồng MassTransit từ publisher đến consumer, sử dụng `TestIntegrationEvent` có sẵn trong project. Mọi lệnh trong doc này đã được chạy thực tế và output được capture từ môi trường local.

---

## Kiến trúc luồng demo

```
curl POST /async/test/publish
    └─► AsyncGateway (IEventBus.PublishAsync)
         └─► RabbitMQ Exchange: Hdos.Contracts.IntegrationEvents:TestIntegrationEvent [fanout]
              └─► Queue: test
                   └─► NotificationService.TestConsumer
                        └─► TestIntegrationEventHandler → Console.WriteLine("Test integrations")
```

**File liên quan:**

| Vai trò | File |
|---|---|
| Publisher endpoint | `src/ApiGateway/Controllers/TestController.cs` |
| Contract | `src/BuildingBlocks/Contracts/IntegrationEvents/TestIntegrationEvent.cs` |
| Consumer | `src/Services/NotificationService/NotificationService.Infrastructure/Consumers/TestConsumer.cs` |
| Handler | `src/Services/NotificationService/NotificationService.Application/EventHandlers/TestIntegrationEventHandler.cs` |

---

## Prerequisite

### 1. Khởi động services

```bash
docker compose up -d
```

Đợi tất cả services `Up` (khoảng 20–30 giây):

```bash
docker compose ps --format "table {{.Name}}\t{{.Status}}"
```

Output mong đợi:
```
NAME                         STATUS
hdos-asyncgateway-1          Up X seconds
hdos-authservice-1           Up X seconds
hdos-keycloak                Up X seconds
hdos-notificationservice-1   Up X seconds
hdos-orderservice-1          Up X seconds
hdos-nginx                   Up X seconds
hdos-rabbitmq                Up X seconds (healthy)
hdos-sqlserver               Up X seconds (healthy)
```

### 2. Verify RabbitMQ đã tạo queue `test`

Vào `http://localhost:15672` (guest/guest) → **Queues** → tìm queue `test`.

Nếu chưa có, service chưa kết nối được RabbitMQ — kiểm tra log:
```bash
docker compose logs notificationservice --tail=30 | grep -i "rabbit\|error\|fail"
```

---

## Bước 1 — Lấy JWT Token

Keycloak có sẵn test account `testuser@hdos.dev / Test1234!`. Lấy token qua **Keycloak port 8080** (không qua nginx):

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/realms/hdos/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password\
&client_id=hdos-backend\
&client_secret=hdos-backend-dev-secret\
&username=testuser@hdos.dev\
&password=Test1234!" \
  | jq -r '.access_token')

echo "Token length: ${#TOKEN}"
```

Output mong đợi:
```
Token length: 973   (hoặc tương tự, > 100)
```

Verify issuer trong token (phải là `http://keycloak:8080/realms/hdos`):
```bash
echo $TOKEN | cut -d. -f2 | base64 -d 2>/dev/null | jq '{iss, email}'
```

```json
{
  "iss": "http://keycloak:8080/realms/hdos",
  "email": "testuser@hdos.dev"
}
```

> **Lưu ý:** Lấy token qua `http://localhost:8080` (direct) thay vì qua nginx `https://localhost:8443`. Issuer trong token phải khớp với `Keycloak__Authority=http://keycloak:8080/realms/hdos` mà services đang dùng. Xem [doc 16](./16-https-ssl.md) để hiểu tại sao.

---

## Bước 2 — Publish Event

```bash
curl -s -X POST https://localhost:8443/async/test/publish \
  -H "Authorization: Bearer $TOKEN" \
  -k | jq .
```

**Response thực tế (202 Accepted):**
```json
{
  "success": true,
  "data": {
    "eventId": "662401c1-11fc-4367-a1c9-099dde098d3c",
    "correlationId": "9d1c0a82-72f4-4065-9830-f4e8463636bf",
    "message": "TestIntegrationEvent published. Check NotificationService logs."
  },
  "errorCode": null,
  "errorMessage": null
}
```

`eventId` và `correlationId` là UUID ngẫu nhiên mỗi lần gọi.

---

## Bước 3 — Kiểm tra Consumer nhận message

```bash
docker compose logs notificationservice --tail=10 --follow
```

Dừng bằng `Ctrl+C` khi thấy dòng này xuất hiện:

```
notificationservice-1  | Test integrations
```

Đây là output từ `TestIntegrationEventHandler.HandleAsync()`:

```csharp
public Task HandleAsync(TestIntegrationEvent @event, CancellationToken ct)
{
    Console.WriteLine("Test integrations");   // ← dòng này
    return Task.CompletedTask;
}
```

Nếu muốn lọc chính xác hơn (không follow):
```bash
docker compose logs notificationservice --tail=20 | grep "Test integrations"
```

---

## Bước 4 — Verify trên RabbitMQ Management

Mở `http://localhost:15672` → đăng nhập `guest/guest`.

### Exchange

**Exchanges** → tìm `Hdos.Contracts.IntegrationEvents:TestIntegrationEvent`:

| Field | Giá trị |
|---|---|
| Type | fanout |
| Durable | Yes |
| Bindings | → `test` (queue) |

### Queue

**Queues** → chọn `test`:

| Field | Giá trị sau khi publish |
|---|---|
| Ready | 0 (message đã được consume) |
| Unacked | 0 |
| Total | 0 |
| Consumer count | 1 (NotificationService đang listen) |

`Ready = 0` nghĩa là consumer đã nhận và ack thành công — không còn message tồn đọng.

---

## Script tổng hợp (chạy một lần)

```bash
#!/bin/bash
set -e

# 1. Get token
TOKEN=$(curl -s -X POST http://localhost:8080/realms/hdos/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=hdos-backend&client_secret=hdos-backend-dev-secret&username=testuser@hdos.dev&password=Test1234!" \
  | jq -r '.access_token')

echo "[1] Token acquired (length ${#TOKEN})"

# 2. Publish
RESP=$(curl -s -X POST https://localhost:8443/async/test/publish \
  -H "Authorization: Bearer $TOKEN" -k)

EVENT_ID=$(echo $RESP | jq -r '.data.eventId')
echo "[2] Published eventId=$EVENT_ID"

# 3. Wait and check log
sleep 2
LOG=$(docker compose logs notificationservice --tail=5 2>/dev/null | grep "Test integrations" || true)

if [ -n "$LOG" ]; then
  echo "[3] Consumer confirmed: $LOG"
else
  echo "[3] WARNING: 'Test integrations' not found in logs — check manually"
fi
```

---

## Troubleshooting

### 401 Unauthorized

```json
{ "error": "Unauthorized", "message": "Valid JWT token required" }
```

**Nguyên nhân phổ biến:**

| Triệu chứng | Nguyên nhân | Fix |
|---|---|---|
| `error="invalid_token"` + `issuer ... is invalid` | Issuer trong token ≠ `Keycloak__Authority` | Lấy token qua `http://localhost:8080`, không qua nginx |
| Token length = 4 | Keycloak chưa sẵn sàng | Đợi thêm 15–30s, kiểm tra `docker compose logs keycloak` |
| `unauthorized_client` | Sai `client_secret` hoặc `client_id` | Dùng đúng `hdos-backend` / `hdos-backend-dev-secret` |

### Consumer không nhận message

```bash
# Kiểm tra queue có message tồn đọng không
curl -s -u guest:guest http://localhost:15672/api/queues/%2F/test | jq '.messages'

# Kiểm tra có consumer nào đang listen không
curl -s -u guest:guest http://localhost:15672/api/queues/%2F/test | jq '.consumer_count'

# Kiểm tra _error queue
curl -s -u guest:guest http://localhost:15672/api/queues/%2F/test_error | jq '.messages'
```

Nếu `messages > 0` và `consumer_count = 0` → NotificationService chưa kết nối RabbitMQ:
```bash
docker compose restart notificationservice
```

Nếu có message trong `test_error` → handler đang throw exception:
```bash
docker compose logs notificationservice --tail=50 | grep -E "ERROR|Exception|fail"
```

### Exchange chưa tồn tại trên RabbitMQ

Exchange được tạo lần đầu khi publisher **hoặc** consumer khởi động và declare topology. Nếu exchange chưa có:
- Chạy `docker compose restart asyncgateway notificationservice` rồi đợi 10s
- Hoặc publish một message — AsyncGateway sẽ tự tạo exchange khi gửi message đầu tiên

---

## Test luồng thực tế hơn (Async Order)

Ngoài TestEvent, luồng async order phản ánh production use case hơn:

```bash
# Cần token của user đã tồn tại trong DB AuthService
TOKEN=$(curl -s -X POST http://localhost:8080/realms/hdos/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=hdos-backend&client_secret=hdos-backend-dev-secret&username=testuser@hdos.dev&password=Test1234!" \
  | jq -r '.access_token')

# Publish OrderCreateRequested
curl -s -X POST https://localhost:8443/async/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"items":[{"productName":"Demo Product","quantity":2,"unitPrice":50000}]}' \
  -k | jq .

# OrderService nhận → tạo order → publish OrderCreated
# NotificationService nhận OrderCreated → ghi notification + push SignalR
docker compose logs orderservice --tail=20
docker compose logs notificationservice --tail=20
```

Xem chi tiết luồng async order: [doc 15 — Async Gateway](./15-async-gateway.md).
