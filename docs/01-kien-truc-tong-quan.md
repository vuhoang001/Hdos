# 01 — Tổng quan kiến trúc

## 1. Sơ đồ tổng

```
                    ┌────────────────┐
   Client (curl,    │   ApiGateway   │   YARP reverse proxy
   Postman, FE) ──► │   :5000 (REST) │   /auth/* /orders/* /notifications/*
                    └───────┬────────┘
                            │ HTTP/1.1
        ┌───────────────────┼────────────────────────┐
        ▼                   ▼                        ▼
 ┌──────────────┐    ┌──────────────┐       ┌────────────────────┐
 │ AuthService  │    │ OrderService │       │ NotificationService│
 │ REST :5101   │    │ REST :5102   │       │ REST :5103         │
 │ gRPC :5111   │◄───│ gRPC client  │       │ (consumer only)    │
 └──────┬───────┘    └──────┬───────┘       └─────────┬──────────┘
        │ EF Core           │ EF Core                 │ EF Core
        ▼                   ▼                         ▼
   ┌─────────┐         ┌─────────┐               ┌──────────────┐
   │ AuthDb  │         │ OrderDb │               │ NotificationDb│
   └─────────┘         └─────────┘               └──────────────┘

        │  publish              │  publish              ▲
        │  UserRegistered       │  OrderCreated         │ consume
        │  UserLoggedIn         │                       │ (3 queue)
        ▼                       ▼                       │
                    ┌──────────────────┐
                    │ RabbitMQ         │
                    │ exchange:        │
                    │   hdos.events    │ topic
                    └──────────────────┘
```

## 2. Các thành phần

| Thành phần              | Vai trò                                                                           |
|-------------------------|-----------------------------------------------------------------------------------|
| **ApiGateway**          | Cổng vào duy nhất (YARP). Forward HTTP theo prefix path tới service tương ứng.    |
| **AuthService**         | Quản lý user: register, login, get user. Publish `UserRegistered`, `UserLoggedIn`. Expose **gRPC `UserService`** để service khác lookup user. |
| **OrderService**        | Quản lý đơn hàng. Trước khi tạo order **gọi gRPC sang Auth** verify user. Publish `OrderCreated`. |
| **NotificationService** | Chỉ consume các integration event từ RabbitMQ và lưu Notification. Có 1 endpoint REST đọc danh sách. |
| **BuildingBlocks**      | Thư viện dùng chung: `SharedKernel`, `Contracts` (event + proto), `Common`.        |
| **SQL Server**          | Một instance, 3 database tách biệt (Auth/Order/Notification).                       |
| **RabbitMQ**            | Topic exchange `hdos.events`, mỗi consumer tự bind queue riêng.                    |

## 3. Hai dạng giao tiếp

### 3.1 Đồng bộ — request/response

Có **hai kênh đồng bộ** trong hệ thống:

1. **HTTP (Client → Service)**
   Client gọi qua Gateway `:5000`, Gateway forward sang service.
   - Dùng cho mọi API public (REST + Swagger).
   - Wrapper response chuẩn: `ApiResponse<T>`.

2. **gRPC (Service → Service)**
   OrderService → AuthService qua port `5111` (HTTP/2 plaintext).
   - Hợp đồng định nghĩa trong `src/BuildingBlocks/Contracts/Protos/users.proto`.
   - Client/server code được `Grpc.Tools` sinh tự động khi build.
   - Chi tiết → [07 — gRPC](./07-grpc.md).

### 3.2 Bất đồng bộ — event-driven

Mọi cập nhật mà service khác cần biết đều được publish dưới dạng
`IntegrationEvent` qua RabbitMQ topic exchange `hdos.events`.

| Event                        | Publisher    | Consumer            |
|------------------------------|--------------|---------------------|
| `UserRegisteredIntegrationEvent` | AuthService  | NotificationService |
| `UserLoggedInIntegrationEvent`   | AuthService  | NotificationService |
| `OrderCreatedIntegrationEvent`   | OrderService | NotificationService |

Routing key = tên class event. Mỗi consumer có queue riêng (vd
`notification.order-created`) để tránh competing consumer giữa các handler khác nhau.

Chi tiết → [08 — RabbitMQ](./08-rabbitmq.md).

## 4. Clean Architecture trong từng service

```
Service/
├── *.Domain          ← Entities, Aggregates, Value Objects, Domain Events,
│                       Repository interfaces. Chỉ phụ thuộc SharedKernel.
├── *.Application     ← CQRS (MediatR Command/Query + Handler), Validators,
│                       DTOs, abstractions cần Infrastructure implement.
│                       Phụ thuộc Domain + Common + Contracts.
├── *.Infrastructure  ← EF Core DbContext + Repository, RabbitMQ consumer,
│                       gRPC client adapter, password hasher…
│                       Phụ thuộc Application + Domain + EF/Grpc/Rabbit.
└── *.API             ← Program.cs, Controllers, gRPC services, middleware.
                        Compose tất cả lại, không chứa business logic.
```

**Dependency rule** (chỉ chảy vào trong, không bao giờ ra ngoài):

```
API ──► Application ──► Domain
 │           │
 └──► Infrastructure ──► Application ──► Domain
                          ▲
                          │ implement abstractions
```

**Hệ quả thực tế**:

- Đổi DB từ SQL Server sang Postgres → chỉ sửa `Infrastructure` + connection string.
- Đổi gRPC sang HTTP cho user lookup → chỉ thay implementation của
  `IUserLookupService` trong `OrderService.Infrastructure`. Application & Domain
  không hề biết.
- Đổi RabbitMQ sang Kafka → sửa `BuildingBlocks/Common/Messaging`, các service
  không phải thay đổi nếu giữ nguyên `IEventBus` / `RabbitMqConsumerHostedService` API.

## 5. Cross-cutting concerns

| Concern                | Thực hiện ở                                                  |
|------------------------|--------------------------------------------------------------|
| Logging                | `Common/Logging/SerilogConfig.UseHdosLogging(serviceName)`   |
| Request log            | `Common/Middleware/RequestLoggingMiddleware`                 |
| Exception → JSON       | `Common/Middleware/ExceptionHandlingMiddleware`              |
| Response chuẩn         | `Common/Responses/ApiResponse<T>`                            |
| MediatR LoggingBehavior| `Common/Behaviors/LoggingBehavior`                           |
| FluentValidation       | `Common/Behaviors/ValidationBehavior`                        |

Cả ba service đều bật chung qua `builder.UseHdosLogging("ServiceName")` và
`app.UseHdosMiddleware()` (extension trong `BuildingBlocks/Common`).

## 6. Cổng & port mặc định

| Service             | REST | gRPC | DB    | RabbitMQ |
|---------------------|------|------|-------|----------|
| ApiGateway          | 5000 | —    | —     | —        |
| AuthService         | 5101 | 5111 | AuthDb | publisher |
| OrderService        | 5102 | —    | OrderDb | publisher |
| NotificationService | 5103 | —    | NotificationDb | consumer (3 queue) |
| SQL Server          | 1433 | —    | —     | —        |
| RabbitMQ            | 5672 (AMQP), 15672 (UI) | — | — | — |

Port có thể đổi qua `Kestrel:RestPort`/`Kestrel:GrpcPort` trong `appsettings.*.json`
hoặc env var (`Kestrel__RestPort`, `Kestrel__GrpcPort`).
