# 15 — Realtime với SignalR (NotificationService)

`NotificationService` không chỉ persist noti vào DB nữa — sau khi consume xong
một `IntegrationEvent` từ RabbitMQ, nó **đẩy ngay payload** về client đang
online qua SignalR.

```
Auth/Order publish event ─► RabbitMQ ─► NotificationService.Consumer
                                                │
                                                ▼
                              Handler.HandleAsync
                                  ├── Notification.Create + persist
                                  └── INotificationPusher.PushToUserAsync
                                                │
                                                ▼
                            IHubContext<NotificationHub>
                                  Clients.User(email).SendAsync("notification", dto)
                                                │
                                                ▼
                                  Client (browser, mobile…)
```

## 1. Endpoint hub

| Mục              | Giá trị                                           |
|------------------|---------------------------------------------------|
| Hub class        | `Hdos.NotificationService.API.Hubs.NotificationHub` |
| Path mount       | `/notifications/hubs/notifications`               |
| Qua API Gateway  | `http://localhost:5000/notifications/hubs/notifications` |
| Auth             | Bắt buộc JWT (`[Authorize]`)                      |
| Transport        | WebSocket (ưu tiên), fallback Server-Sent Events / Long-polling |

Hub chỉ expose **1 method server-side**: `Ping()` trả `"pong"` — dùng để smoke
test connection. Toàn bộ payload chính thức là **server → client**, không phải
client → server.

## 2. Events từ server → client

| Event name      | Payload                              | Khi nào fire                                 |
|-----------------|--------------------------------------|----------------------------------------------|
| `notification`  | `NotificationDto` (xem mục 6)        | Mỗi khi handler tạo và persist xong noti     |

Hiện có 3 nguồn fire `notification`:

| Trigger event                    | Recipient (UserIdentifier) | Subject mẫu                          |
|----------------------------------|----------------------------|--------------------------------------|
| `UserLoggedInIntegrationEvent`   | `event.Email`              | "New login on your account"          |
| `UserRegisteredIntegrationEvent` | `event.Email`              | "Welcome to Hdos!"                   |
| `OrderCreatedIntegrationEvent`   | `event.CustomerEmail`      | "Order {orderId} confirmed"          |

## 3. Định danh user — vì sao dùng email

SignalR mặc định lấy `ClaimTypes.NameIdentifier` (= `sub` = userId GUID) làm
`Context.UserIdentifier`. Trong NotificationService, entity `Notification` lại
dùng **email** làm `Recipient`.

`EmailUserIdProvider` (file `Hubs/EmailUserIdProvider.cs`) override lựa chọn:

```csharp
public string? GetUserId(HubConnectionContext connection) =>
    connection.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
 ?? connection.User.FindFirst(ClaimTypes.Email)?.Value;
```

Đăng ký singleton ở `Program.cs`:

```csharp
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, EmailUserIdProvider>();
```

→ Handler chỉ cần một dòng để target đúng user:

```csharp
await _hub.Clients.User(notification.Recipient).SendAsync("notification", dto, ct);
```

## 4. Auth qua WebSocket — `?access_token=`

Browser **không cho gắn header `Authorization`** vào upgrade request của
WebSocket. Convention của ASP.NET Core SignalR là gửi token qua query string:

```
ws://gateway/notifications/hubs/notifications?id=<connId>&access_token=<jwt>
```

`JwtBearer` mặc định chỉ đọc header `Authorization` nên ta hook vào event
`OnMessageReceived` để fall-back sang query khi path chứa `/hubs/`.

File: `src/BuildingBlocks/Common/Auth/JwtAuthExtensions.cs`

```csharp
o.Events = new JwtBearerEvents
{
    OnMessageReceived = ctx =>
    {
        var accessToken = ctx.Request.Query["access_token"];
        var path = ctx.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) && path.HasValue &&
            path.Value!.Contains("/hubs/", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Token = accessToken;
        }
        return Task.CompletedTask;
    }
};
```

Cài đặt nằm ở `Common` → áp dụng cho **cả Gateway và Service**. Cả hai chặng
xác thực đều chấp nhận token từ query string.

## 5. Đường đi qua API Gateway (YARP)

Gateway dùng catch-all route `/notifications/{**catch-all}` (xem
[09-api-gateway.md](./09-api-gateway.md)) → tự cover luôn
`/notifications/hubs/notifications`. Không cần thêm route mới.

YARP forward WebSocket out-of-the-box; gateway chỉ cần `app.UseWebSockets()`
trước `MapReverseProxy()` để upgrade pipeline được wire đúng thứ tự.

```csharp
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
...
app.MapReverseProxy();
```

## 6. DTO trên dây

Payload event `notification` là JSON của `NotificationDto`:

```json
{
  "id": "f1cb1c9e-72a9-4f7d-9f54-8c21d6a4f17e",
  "recipient": "alice@example.com",
  "subject": "Order f1cb1c9e... confirmed",
  "body": "Thanks for your order!\nTotal: 199.99\nItems:\n - Coffee x2 @ 4.50",
  "channel": "Email",
  "status": "Sent",
  "createdAtUtc": "2026-05-08T08:42:13Z",
  "sentAtUtc":    "2026-05-08T08:42:13Z"
}
```

Tên field theo mặc định Pascal → camel của System.Text.Json.

## 7. Kiến trúc — Application không phụ thuộc SignalR

Application define **port** `INotificationPusher`:

```csharp
// src/Services/NotificationService/NotificationService.Application/Realtime/INotificationPusher.cs
public interface INotificationPusher
{
    Task PushToUserAsync(string userEmail, NotificationDto notification, CancellationToken ct = default);
}
```

API tầng implement bằng SignalR:

```csharp
// src/Services/NotificationService/NotificationService.API/Hubs/SignalRNotificationPusher.cs
public sealed class SignalRNotificationPusher(IHubContext<NotificationHub> hub, ILogger<...> logger) : INotificationPusher
```

Đăng ký DI ở `Program.cs`:

```csharp
builder.Services.AddScoped<INotificationPusher, SignalRNotificationPusher>();
```

Handler nhận pusher qua constructor, gọi sau khi đã `SaveChangesAsync` để đảm
bảo client chỉ thấy noti đã thực sự persist.

## 8. Client mẫu

### 8.1 JavaScript (browser)

```bash
npm i @microsoft/signalr
```

```ts
import * as signalR from "@microsoft/signalr";

const token = localStorage.getItem("hdos_jwt")!; // lấy từ response /auth/login

const conn = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5000/notifications/hubs/notifications", {
    accessTokenFactory: () => token,
  })
  .withAutomaticReconnect()
  .configureLogging(signalR.LogLevel.Information)
  .build();

conn.on("notification", (n) => {
  console.log("new noti:", n.subject, n.body);
});

await conn.start();
console.log("SignalR connected, state:", conn.state);

// (optional) gọi method server-side
const reply = await conn.invoke<string>("Ping");
console.log("server reply:", reply); // "pong"
```

`accessTokenFactory` được gọi mỗi lần SignalR cần token — kể cả khi reconnect
sau khi token hết hạn. Trả token mới từ refresh-token nếu có.

### 8.2 .NET client (ví dụ test)

```csharp
using Microsoft.AspNetCore.SignalR.Client;

var token = "<jwt>";
var conn = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/notifications/hubs/notifications",
        opts => opts.AccessTokenProvider = () => Task.FromResult<string?>(token))
    .WithAutomaticReconnect()
    .Build();

conn.On<NotificationDto>("notification", n =>
    Console.WriteLine($"[{n.Subject}] {n.Body}"));

await conn.StartAsync();
Console.WriteLine($"State = {conn.State}");
```

### 8.3 Lệnh test nhanh bằng `curl` + `wscat`

1. Lấy JWT:

   ```bash
   TOKEN=$(curl -s http://localhost:5000/auth/login \
     -H 'Content-Type: application/json' \
     -d '{"email":"alice@example.com","password":"P@ssw0rd"}' \
     | jq -r .data.token)
   ```

2. Negotiate (HTTP):

   ```bash
   curl -X POST "http://localhost:5000/notifications/hubs/notifications/negotiate?negotiateVersion=1" \
        -H "Authorization: Bearer $TOKEN"
   ```

3. Mở WebSocket bằng [wscat](https://github.com/websockets/wscat) — note: phải
   thêm `?id=<connectionId từ negotiate>&access_token=$TOKEN`. Trong demo
   thường chỉ cần dùng SDK ở mục 8.1 / 8.2 vì SDK tự làm bước này.

## 9. Sequence end-to-end (Order → Realtime noti)

```
Browser ── POST /orders ──► Gateway ──► OrderService
                                               │
                                               │ persist Order
                                               │
                                               │ publish OrderCreatedIntegrationEvent
                                               ▼
                                            RabbitMQ
                                               │
                                               ▼
                                    NotificationService
                              OrderCreatedConsumer.OnMessageAsync
                                  ├── handler.HandleAsync
                                  │     ├── Notification.Create + Save
                                  │     └── INotificationPusher.PushToUserAsync(email, dto)
                                  ▼
                          IHubContext<NotificationHub>
                                  │
                                  ▼
                        Browser nhận event "notification"
                            (qua connection đã open từ trước)
```

Người dùng thấy toast/noti **ngay** mà không phải poll `/notifications`.

## 10. Lưu ý vận hành

- **Scale-out nhiều instance NotificationService**: SignalR mặc định chỉ
  broadcast trong cùng process. Khi chạy >1 replica cần thêm backplane:
  Redis (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) hoặc Azure
  SignalR. Hiện hệ thống chạy 1 replica nên chưa wire.
- **YARP load balancing với sticky session**: nếu sau này có nhiều replica
  notification mà chưa wire backplane, đặt `LoadBalancingPolicy: "Cookie"`
  ở `notifications-cluster` để 1 client luôn vào 1 replica.
- **Token expire trong khi connection còn live**: SignalR sẽ disconnect khi
  expired. Client `withAutomaticReconnect()` + `accessTokenFactory` trả token
  mới sẽ tự reconnect.
- **CORS**: nếu frontend chạy domain khác Gateway, cần
  `services.AddCors(...)` + `app.UseCors(...)` ở Gateway, và phải bật
  `AllowCredentials()` để WebSocket hoạt động.
- **Health check**: SignalR không có endpoint health riêng — dùng
  `GET /notifications/health` (đã có) để check service alive trước khi connect.
