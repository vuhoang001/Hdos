# 14. Realtime Notifications — Server-Sent Events (SSE)

NotificationService dùng **SSE (Server-Sent Events)** để push notification realtime từ server xuống browser.
SSE là HTTP/1.1 đơn giản — không cần WebSocket upgrade, không cần thư viện client đặc biệt.

---

## Kiến trúc

```
Browser
  └── GET /notifications/sse?access_token=...   (HTTP connection mở mãi)
        │
      Nginx
        │  proxy_buffering off  +  proxy_read_timeout 24h
        │
      NotificationService
        ├── SseController          — nhận connection, stream event
        ├── SseConnectionManager   — track tất cả connection đang mở
        └── SseNotificationPusher  — implements INotificationPusher
              │
        (event từ RabbitMQ → MassTransit Consumer → INotificationPusher → SSE)
```

---

## Cách hoạt động

1. Browser gọi `GET /notifications/sse?access_token=<jwt>` — connection không đóng
2. Nginx giữ connection, tắt buffering để event đến client ngay lập tức
3. Khi có notification mới (từ RabbitMQ), `SseNotificationPusher` ghi vào channel của user
4. `SseController` đọc channel và gửi SSE event xuống browser
5. Browser nhận `event: notification` và xử lý

---

## Auth

`EventSource` API của browser **không support custom header** → token truyền qua query param:

```
GET /notifications/sse?access_token=eyJhbGc...
```

`JwtAuthExtensions.OnMessageReceived` tự động đọc `access_token` từ query string khi path chứa `/sse`.

---

## SSE event format

```
event: notification
data: {"type":"notification","payload":{...},"occurredAtUtc":"2026-05-19T07:00:00Z"}

```

Mỗi event kết thúc bằng **2 dòng trống** (`\n\n`) — đây là spec của SSE.

### NotificationEnvelope

```json
{
  "type": "notification",
  "payload": {
    "id": "uuid",
    "recipient": "user@example.com",
    "subject": "Đơn hàng #123 đã được xác nhận",
    "body": "...",
    "channel": "SSE",
    "status": "Sent",
    "createdAtUtc": "...",
    "sentAtUtc": "..."
  },
  "occurredAtUtc": "2026-05-19T07:00:00Z",
  "correlationId": null
}
```

---

## Frontend integration

```typescript
// Kết nối SSE
const token = getAccessToken(); // lấy từ Keycloak
const es = new EventSource(
  `https://<host>/notifications/sse?access_token=${token}`
);

// Lắng nghe event notification
es.addEventListener('notification', (e: MessageEvent) => {
  const envelope = JSON.parse(e.data);
  console.log(envelope.payload); // NotificationDto
});

// Xử lý mất kết nối — browser tự reconnect sau 3s
es.onerror = (err) => {
  console.error('SSE error', err);
  // Không cần manually reconnect — EventSource tự làm
};

// Đóng khi logout
es.close();
```

---

## Nginx config

```nginx
location = /notifications/sse {
    if ($request_method = OPTIONS) { return 418; }
    proxy_pass         http://notificationservice;
    proxy_buffering    off;   # bắt buộc — event đến client ngay, không bị buffer
    proxy_read_timeout 24h;   # giữ connection lâu dài
    proxy_cache        off;
}
```

**Tại sao `proxy_buffering off`?**
Nginx mặc định gom response lại trước khi gửi cho client. Với SSE, mỗi event cần đến client ngay — nếu để buffering, event bị giữ lại cho đến khi buffer đầy.

---

## Multiple connections

Một user có thể mở nhiều tab cùng lúc — mỗi tab là một SSE connection riêng.
`SseConnectionManager` dùng `ConcurrentDictionary<userId, ConcurrentDictionary<connectionId, Channel>>` để track tất cả.

```
user@hdos.dev
  ├── connectionId: abc-123  (tab 1)
  └── connectionId: def-456  (tab 2)
```

Khi push notification, cả 2 tab đều nhận.

---

## Test trong Development

`TestBroadcastService` tự động broadcast test notification mỗi 5 giây đến tất cả client đang kết nối. Chỉ active khi `ASPNETCORE_ENVIRONMENT=Development`.

Test nhanh bằng curl:

```bash
# Lấy token
TOKEN=$(curl -s -X POST http://localhost:8080/realms/hdos/protocol/openid-connect/token \
  -d "grant_type=password&client_id=hdos-backend&username=testuser&password=<pass>" \
  | jq -r '.access_token')

# Connect SSE (giữ terminal mở, event sẽ hiện ra)
curl -N "https://192.168.100.60:8443/notifications/sse?access_token=$TOKEN" -k
```

---

## So sánh SSE vs WebSocket (SignalR)

| | SSE | WebSocket / SignalR |
|---|---|---|
| Hướng giao tiếp | Server → Client (1 chiều) | 2 chiều |
| Protocol | HTTP/1.1 | Upgrade → WebSocket |
| Browser API | `EventSource` (built-in) | Cần thư viện |
| Auth | Query param `?access_token=` | Query param hoặc cookie |
| Nginx config | `proxy_buffering off` | `Upgrade` + `Connection` headers |
| Reconnect | Tự động (built-in) | Cần implement |
| Phù hợp với | Push notification 1 chiều | Chat, game, cộng tác real-time |

SSE phù hợp với Hdos vì notification chỉ cần **server → client** — không cần client gửi message ngược lại.
