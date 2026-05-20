# 01 — Tổng quan kiến trúc

## Hệ thống làm gì?

Hdos là nền tảng quản lý bệnh viện gồm các nghiệp vụ:
- Xác thực người dùng (đăng ký, đăng nhập, JWT)
- Quản lý đặt khám / đơn hàng
- Thông báo real-time (SignalR)
- Quản lý module M01 (cấp cứu, phòng khám, dashboard)

---

## Sơ đồ tổng quan

```
┌─────────────────────────────────────────────────────────────────┐
│                        Client Layer                              │
│           Browser / Mobile App / Postman / curl                 │
└───────────────────────────┬─────────────────────────────────────┘
                            │ HTTPS (port 8443) / HTTP (8080 → redirect)
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    nginx (Reverse Proxy)                         │
│  • TLS termination + redirect HTTP→HTTPS                         │
│  • CORS authority (whitelist origin, strip upstream CORS)        │
│  • Route theo prefix: /auth /orders /notifications /m01 /async   │
│  • KHÔNG verify JWT — services tự lo                             │
└──────┬──────────┬──────────────┬──────────────┬─────────────────┘
       │          │              │              │
       ▼          ▼              ▼              ▼
  AuthService  OrderService  Notification   M01Service / AsyncGW
       │  Mỗi service: JwtBearer verify JWT + đọc claim "permission"
       │              → [Authorize(Policy = HdosPermissions.X)]
       │          │              │              │
       └──────────┴──────────────┴──────────────┘
                            │
               ┌────────────┼────────────┐
               ▼            ▼            ▼
           SQL Server    RabbitMQ      gRPC
         (4 databases)  (topic exch)  (Auth→Order)
```

---

## Các công nghệ và lý do chọn

### .NET 8 + C#
**Lý do:** Ecosystem mạnh cho enterprise, performance cao (top benchmarks), native gRPC support, EF Core tích hợp sẵn. Team có sẵn kinh nghiệm .NET.

### Clean Architecture (DDD)
**Lý do:** Tách biệt Domain logic khỏi Infrastructure. Khi đổi database, message broker, hay framework — core business logic không thay đổi. Dễ test vì domain không phụ thuộc framework.

Mỗi service có 4 layer:
```
Domain         → Entity, ValueObject, AggregateRoot, DomainEvent
Application    → Command/Query (MediatR), Validator, UseCase
Infrastructure → DbContext, Repository, RabbitMQ, gRPC client
API            → Controller, Middleware, Program.cs
```

### Docker + Docker Compose
**Lý do:** Mọi developer chạy cùng một environment. Không còn "chạy được trên máy tôi". Production dùng overlay file để inject production config mà không sửa code.

### nginx (thay vì YARP C# Gateway)
**Lý do:** YARP là reverse proxy viết bằng C# — cần build, test, deploy như một service. nginx là battle-tested, configuration-based, reload không cần restart. Khi thêm route mới chỉ sửa file text và `nginx -s reload`. Nhẹ hơn ~10x về memory. Chi tiết xem [05 — Nginx Gateway](./05-nginx-gateway.md).

### SQL Server (một instance, nhiều databases)
**Lý do:** Mỗi service có database riêng (Database-per-Service pattern) đảm bảo bounded context. Dùng chung một SQL Server instance để tiết kiệm tài nguyên trong môi trường demo/staging. Production có thể tách ra instance riêng.

### RabbitMQ (Async messaging)
**Lý do:** Khi OrderService cần thông báo cho NotificationService, nếu dùng HTTP trực tiếp: OrderService phải biết địa chỉ NotificationService, nếu NotificationService down thì Order fail. RabbitMQ giải quyết bằng publish-subscribe: OrderService publish event, NotificationService consume sau. Hai bên decoupled hoàn toàn.

### gRPC (Sync internal calls)
**Lý do:** OrderService cần verify user tồn tại trước khi tạo order — đây là synchronous call (cần kết quả ngay). gRPC dùng binary protocol (Protobuf), faster hơn JSON REST, strongly-typed contract (`.proto` file), native code generation. Chi tiết xem [07 — Giao tiếp nội bộ](./07-giao-tiep-noi-bo.md).

### OpenTelemetry + Prometheus + Loki + Tempo + Grafana
**Lý do:** Hệ thống microservices không thể debug bằng console.log như monolith. Cần:
- Biết request đang chậm ở service nào (Distributed Tracing → Tempo)
- Biết tỉ lệ lỗi, latency theo thời gian (Metrics → Prometheus)
- Biết log của request cụ thể là gì (Logs → Loki)
- Xem tất cả trên một màn hình (Grafana)

Chi tiết xem [08 — Quan sát hệ thống](./08-quan-sat-he-thong.md).

---

## Nguyên tắc thiết kế

### 1. Mỗi service là một bounded context
AuthService không biết gì về Order. OrderService không truy cập trực tiếp vào database của AuthService. Giao tiếp chỉ qua API (gRPC/RabbitMQ).

### 2. Defense in depth cho bảo mật
- Nginx kiểm tra JWT trước khi forward request
- Mỗi service cũng validate JWT riêng
- Nếu ai bypass nginx (VPN nội bộ, container escape), service vẫn reject unauthorized request

### 3. Fail fast, not silently
Tất cả exception đều được bắt bởi `ExceptionHandlingMiddleware` và trả về JSON chuẩn `ApiResponse`. Không để lộ stack trace ra ngoài.

### 4. Configuration qua environment variables
Không hardcode secret, connection string, endpoint. Tất cả inject qua env vars. Local dùng `docker-compose.yml`, server dùng `docker-compose.server.yml` + `.env` files.

---

## Port mapping

| Port | Service | Ghi chú |
|------|---------|---------|
| 5000 | nginx gateway | Duy nhất expose ra ngoài |
| 1433 | SQL Server | Dev only |
| 5672 | RabbitMQ AMQP | Internal |
| 15672 | RabbitMQ Management UI | Dev only |
| 9090 | Prometheus | Monitoring |
| 3030 | Grafana | Monitoring |
| 3100 | Loki | Monitoring |
| 3200 | Tempo | Monitoring |
| 4317 | OTLP gRPC | Tracing ingestion |
| 4318 | OTLP HTTP | Tracing ingestion |
