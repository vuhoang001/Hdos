# 14 — SignalR Realtime (NotificationService)

---

## Tổng quan

NotificationService expose một **SignalR Hub** để push notification real-time từ server xuống client mà không cần polling. Server chủ động gửi khi có event từ RabbitMQ — client chỉ cần giữ kết nối WebSocket.

```
RabbitMQ Event
  ↓ Consumer (UserLoggedIn / UserRegistered / OrderCreated)
  ↓ Lưu Notification vào DB
  ↓ INotificationPusher.PushToUserAsync(email, dto)
  ↓ SignalRNotificationPusher → IHubContext<NotificationHub>
  ↓ WebSocket (SignalR)
  ↓ Frontend nhận event "notification"
```

---

## Hub endpoint


| Property           | Giá trị                                                   |
| ------------------ | ----------------------------------------------------------- |
| Path (qua Gateway) | `ws://localhost/notifications/hubs/notifications`           |
| Path (direct)      | `ws://localhost:PORT/notifications/hubs/notifications`      |
| Auth               | JWT bắt buộc (`[Authorize]`)                              |
| Transport          | WebSocket (ưu tiên) → Server-Sent Events → Long Polling |

---

## Xác thực khi kết nối

SignalR dùng WebSocket nên không thể đặt `Authorization` header sau khi upgrade. Token phải truyền qua **query string** khi negotiate.

```
GET /notifications/hubs/notifications?access_token=<JWT>
```

`JwtAuthExtensions` trong Common tự detect path có `/hubs/` và lấy token từ query string thay vì header.

---

## Envelope chuẩn (`SignalREnvelope<T>`)

Mọi message từ server đều bọc trong `SignalREnvelope<T>`. Client parse field `type` để biết xử lý gì, không cần đoán cấu trúc.

### Định nghĩa (C#)

```csharp
// NotificationService.Application/DTOs/SignalREnvelope.cs
public sealed record SignalREnvelope<T>(
    string Type,           // Tên event, snake_case
    T Payload,             // Dữ liệu nghiệp vụ
    DateTime OccurredAtUtc, // Timestamp server (UTC)
    string? CorrelationId = null // Trace ID, nullable
);
```

### JSON shape

```json
{
  "type": "notification",
  "occurredAtUtc": "2026-05-12T10:30:00.000Z",
  "correlationId": null,
  "payload": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "recipient": "user@example.com",
    "subject": "Đăng nhập thành công",
    "body": "Bạn vừa đăng nhập lúc 10:30 ngày 12/05/2026",
    "channel": "SignalR",
    "status": "Sent",
    "createdAtUtc": "2026-05-12T10:30:00.000Z",
    "sentAtUtc": "2026-05-12T10:30:00.001Z"
  }
}
```
### Field `type` hiện tại


| `type`           | Khi nào                                          | Payload           |
| ---------------- | ------------------------------------------------- | ----------------- |
| `"notification"` | Mọi notification push (login, register, order…) | `NotificationDto` |

> Khi thêm event type mới, định nghĩa tại đây và cập nhật bảng này.

### Payload: `NotificationDto`


| Field          | Kiểu                         | Mô tả                             |
| -------------- | ----------------------------- | ----------------------------------- |
| `id`           | `string (UUID)`               | ID notification trong DB            |
| `recipient`    | `string`                      | Email người nhận                 |
| `subject`      | `string`                      | Tiêu đề ngắn                    |
| `body`         | `string`                      | Nội dung chi tiết                 |
| `channel`      | `string`                      | Luôn là`"SignalR"` hiện tại     |
| `status`       | `string`                      | `"Pending"` / `"Sent"` / `"Failed"` |
| `createdAtUtc` | `string (ISO 8601)`           | Thời điểm tạo notification      |
| `sentAtUtc`    | `string (ISO 8601)` \| `null` | Thời điểm push thành công      |

---

## Client → Server methods


| Method   | Tham số | Trả về | Mục đích                     |
| -------- | -------- | -------- | ------------------------------- |
| `Ping()` | —       | `"pong"` | Smoke-test kết nối còn sống |

---

## Cách frontend test nhanh

### 1. HTML thuần (không cần build tool)

Tạo file `signalr-test.html`, mở bằng browser bất kỳ:

```html
<!DOCTYPE html>
<html>
<head>
  <title>SignalR Test</title>
  <script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@8/dist/browser/signalr.min.js"></script>
</head>
<body>
  <h2>SignalR Notification Test</h2>

  <label>JWT Token:</label><br>
  <textarea id="token" rows="3" cols="80" placeholder="Paste JWT here..."></textarea><br><br>

  <button onclick="connect()">Connect</button>
  <button onclick="ping()">Ping</button>
  <button onclick="disconnect()">Disconnect</button>
  <hr>
  <pre id="log"></pre>

  <script>
    let connection = null;

    function log(msg) {
      document.getElementById('log').textContent += '\n' + new Date().toISOString() + ' ' + msg;
    }

    async function connect() {
      const token = document.getElementById('token').value.trim();
      if (!token) { log('[ERROR] Paste JWT token trước'); return; }

      connection = new signalR.HubConnectionBuilder()
        .withUrl('http://localhost/notifications/hubs/notifications', {
          accessTokenFactory: () => token
        })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Information)
        .build();

      // Nhận envelope chuẩn
      connection.on('notification', (envelope) => {
        log('[EVENT] type=' + envelope.type);
        log('        occurredAtUtc=' + envelope.occurredAtUtc);
        log('        payload=' + JSON.stringify(envelope.payload, null, 2));
      });

      connection.onclose(err => log('[CLOSED] ' + (err || 'clean')));
      connection.onreconnecting(err => log('[RECONNECTING] ' + err));
      connection.onreconnected(id => log('[RECONNECTED] connectionId=' + id));

      try {
        await connection.start();
        log('[CONNECTED] connectionId=' + connection.connectionId);
      } catch (e) {
        log('[ERROR] ' + e.message);
      }
    }

    async function ping() {
      if (!connection) { log('[ERROR] Chưa connect'); return; }
      const result = await connection.invoke('Ping');
      log('[PING] → ' + result);
    }

    async function disconnect() {
      if (connection) { await connection.stop(); connection = null; }
    }
  </script>
</body>
</html>
```
**Bước test:**

1. Chạy stack: `docker compose up -d`
2. Lấy JWT: `POST http://localhost/auth/login` → copy `token` từ response
3. Mở `signalr-test.html` trong browser, paste JWT, click **Connect**
4. Trigger event: đăng ký user mới hoặc tạo order
5. Xem notification xuất hiện trong log

---

### 2. React / Vue / Angular (TypeScript)

Cài package:

```bash
npm install @microsoft/signalr
```
Hook mẫu (React):

```typescript
import * as signalR from '@microsoft/signalr';

interface SignalREnvelope<T> {
  type: string;
  payload: T;
  occurredAtUtc: string;
  correlationId: string | null;
}

interface NotificationPayload {
  id: string;
  recipient: string;
  subject: string;
  body: string;
  channel: string;
  status: string;
  createdAtUtc: string;
  sentAtUtc: string | null;
}

export function useNotificationHub(token: string) {
  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/notifications/hubs/notifications', {
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect()
      .build();

    connection.on('notification', (envelope: SignalREnvelope<NotificationPayload>) => {
      console.log('Received:', envelope.type, envelope.payload);
      // dispatch tới store / show toast tùy loại
    });

    connection.start().catch(console.error);

    return () => { connection.stop(); };
  }, [token]);
}
```
---

## Cơ chế user targeting

`EmailUserIdProvider` override `IUserIdProvider` mặc định của SignalR:

```
JWT claim "email" = "user@example.com"
  ↓ EmailUserIdProvider.GetUserId()
  ↓ SignalR UserIdentifier = "user@example.com"
  ↓ IHubContext.Clients.User("user@example.com").SendAsync(...)
  ↓ Chỉ connection của đúng user đó nhận được
```
Server không cần biết connectionId — chỉ cần email. Một user nhiều tab/device đều nhận đủ.

---

## Lưu ý scale


| Scenario       | Cần làm                                                           |
| -------------- | ------------------------------------------------------------------- |
| 1 replica      | Hoạt động ngay, không cần gì thêm                            |
| Nhiều replica | Cần**Redis backplane** (`AddSignalR().AddStackExchangeRedis(...)`) |

Không có Redis backplane: notification chỉ reach user kết nối với replica đang xử lý event, các replica khác không biết.

---

## Liên kết

- [06 — Xác thực & Phân quyền](./06-xac-thuc-phan-quyen.md) — JWT, query string token cho WebSocket
- [05 — nginx Gateway](./05-nginx-gateway.md) — WebSocket upgrade config
- [07 — Giao tiếp nội bộ](./07-giao-tiep-noi-bo.md) — RabbitMQ events trigger SignalR push
- [04 — Các Services](./04-cac-services.md) — NotificationService overview
