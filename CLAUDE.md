# CLAUDE.md — Hdos Microservices Platform

## 0. Quy trình làm việc bắt buộc (LUÔN LUÔN TUÂN THỦ)

Trước khi bắt đầu **bất kỳ task nào** (tạo feature, fix bug, refactor, sửa config...), Claude **phải**:

1. **Trình bày plan** — liệt kê từng bước cụ thể sẽ làm, file nào sẽ tạo/sửa/xóa
2. **Chờ user xác nhận** — không làm gì cho đến khi user nói "ok", "đúng rồi", "làm đi", hoặc tương tự
3. **Thực hiện từng bước một** — xong một bước thì báo cáo kết quả, chờ user confirm trước khi làm bước tiếp theo
4. **Nếu phát hiện điều bất ngờ** giữa chừng (file khác với dự kiến, cần thêm file ngoài plan) → dừng lại, báo cáo, hỏi user trước khi tiếp tục

**Format plan tối thiểu:**
```
Tôi sẽ làm theo các bước sau:

Bước 1: [mô tả] → [file sẽ tạo/sửa]
Bước 2: [mô tả] → [file sẽ tạo/sửa]
Bước 3: [mô tả] → [file sẽ tạo/sửa]

Bạn có muốn tôi bắt đầu không?
```

---

## 1. Project Overview

**Hdos** là nền tảng quản lý bệnh viện (Hospital Management System) xây dựng theo kiến trúc microservices.

### Các service trong hệ thống

| Service | Vai trò |
|---------|---------|
| **AuthService** | Đăng ký, đăng nhập, cấp JWT, quản lý Role/Permission/License |
| **OrderService** | Quản lý đơn hàng/lịch hẹn; xác minh user qua gRPC trước khi tạo order |
| **NotificationService** | Nhận event từ RabbitMQ, lưu notification, push real-time qua SSE |
| **M01Service** | Nghiệp vụ bệnh viện: cấp cứu, phòng khám, dashboard, nhân sự trực |
| **DataMatchingService** | Ingest dữ liệu, dedup bằng SHA-256, match record, engine dashboard + SDUI |
| **DynamicFormService** | Quản lý template form động, trang form, submit form |
| **AsyncGateway** | HTTP → RabbitMQ (202 Accepted pattern); stateless, không có DB |

**Shared Libraries (BuildingBlocks):**
- `SharedKernel` — AggregateRoot, ValueObject, Result\<T\>, Error, IDomainEvent
- `Common` — JWT, CORS, middleware, MassTransit, Prometheus, OpenTelemetry, Serilog
- `Contracts` — IntegrationEvent contracts, gRPC proto definitions

---

## 2. Tech Stack

| Lớp | Công nghệ | Phiên bản |
|-----|-----------|-----------|
| Runtime | .NET / ASP.NET Core | **8.0** |
| CQRS / Mediator | MediatR | 12.4.1 |
| Validation | FluentValidation | 11.10.0 |
| Sync Communication | gRPC + Protobuf (Grpc.Net.ClientFactory) | 2.65.0 |
| Async Messaging | MassTransit + RabbitMQ | 8.2.5 |
| Message Broker | RabbitMQ | 3.13-management |
| ORM | Entity Framework Core | 8.0.10 |
| Database (SQL) | SQL Server (Auth, Order, Notification, M01) | 2022-latest |
| Database (NoSQL) | PostgreSQL (DataMatching, DynamicForm) | 16-alpine |
| Auth | JWT HS256 + ASP.NET Identity (password hashing) | 8.0.10 |
| Real-time | Server-Sent Events (SSE) — built-in | — |
| API Gateway | nginx | 1.27-alpine |
| API Docs | Swashbuckle / Swagger | 6.8.1 |
| Tracing | OpenTelemetry → Grafana Tempo | 1.9.0 |
| Metrics | prometheus-net | 8.2.1 |
| Logging | Serilog → Grafana Loki | 4.0.2 |
| Dashboards | Grafana | latest |
| Testing | xUnit + NSubstitute + FluentAssertions | 2.9.2 / 5.1.0 / 6.12.1 |
| Containerization | Docker + Docker Compose | — |
| CI/CD | GitHub Actions | — |

---

## 3. Project Structure

```
Hdos/
├── src/
│   ├── ApiGateway/                         ← AsyncGateway: HTTP → RabbitMQ
│   │   └── Controllers/                    ← AsyncOrdersController, AsyncNotificationsController
│   ├── BuildingBlocks/
│   │   ├── Common/                         ← Auth, Messaging, Middleware, Monitoring, Persistence
│   │   ├── Contracts/                      ← IntegrationEvents, gRPC proto (users.proto)
│   │   └── SharedKernel/                   ← AggregateRoot, ValueObject, Result<T>, Error
│   └── Services/
│       ├── AuthService/
│       │   ├── AuthService.Domain/
│       │   ├── AuthService.Application/
│       │   ├── AuthService.Infrastructure/
│       │   └── AuthService.API/            ← Port 8080 (REST), 8081 (gRPC)
│       ├── OrderService/
│       │   ├── OrderService.Domain/
│       │   ├── OrderService.Application/
│       │   ├── OrderService.Infrastructure/
│       │   └── OrderService.API/
│       ├── NotificationService/
│       │   ├── NotificationService.Domain/
│       │   ├── NotificationService.Application/
│       │   ├── NotificationService.Infrastructure/
│       │   └── NotificationService.API/
│       ├── M01Service/
│       │   ├── M01Service.Domain/
│       │   ├── M01Service.Application/
│       │   ├── M01Service.Infrastructure/
│       │   └── M01Service.API/
│       ├── DataMatchingService/
│       │   ├── DataMatchingService.Domain/
│       │   ├── DataMatchingService.Application/
│       │   ├── DataMatchingService.Infrastructure/
│       │   └── DataMatchingService.API/
│       └── DynamicFormService/
│           ├── DynamicFormService.Domain/
│           ├── DynamicFormService.Application/
│           ├── DynamicFormService.Infrastructure/
│           └── DynamicFormService.API/
├── tests/                                  ← xUnit test projects (một project per service)
├── docs/                                   ← 32 file tài liệu kỹ thuật (*.md)
├── nginx/                                  ← nginx.conf, TLS certs
├── monitoring/                             ← Prometheus, Loki, Tempo, Grafana configs
├── infra/                                  ← Infrastructure scripts
├── docker-compose.yml                      ← Dev environment (full stack)
├── docker-compose.monitoring.yml           ← Observability overlay
├── docker-compose.server.yml              ← Staging/prod overrides
├── services.json                           ← Map service name → Dockerfile (dùng bởi CI)
└── .github/workflows/                      ← CI (ci.yml) + CD (cd.yml)
```

### Cấu trúc thư mục trong mỗi service (Clean Architecture)

```
ServiceName.Domain/
├── Entities/          ← AggregateRoot, Domain Entities
├── ValueObjects/      ← Immutable value types
├── Events/            ← Domain events (IDomainEvent)
├── Repositories/      ← Repository interfaces (IXxxRepository)
└── Errors/            ← Domain-specific Error constants

ServiceName.Application/
├── Features/
│   ├── SomeAction/
│   │   ├── SomeActionCommand.cs       ← IRequest<Result<T>>
│   │   ├── SomeActionCommandHandler.cs
│   │   └── SomeActionCommandValidator.cs
│   └── SomeQuery/
│       ├── GetSomethingQuery.cs
│       └── GetSomethingQueryHandler.cs
├── DTOs/
└── EventHandlers/     ← Domain event handlers / Integration event handlers

ServiceName.Infrastructure/
├── Persistence/
│   ├── XxxDbContext.cs
│   ├── Configurations/   ← EF Core IEntityTypeConfiguration<T>
│   └── Repositories/     ← Repository implementations
├── Grpc/              ← gRPC client adapters (nếu service gọi service khác)
└── Messaging/         ← MassTransit consumers (nếu có)

ServiceName.API/
├── Controllers/
├── Middleware/        ← Service-specific middleware (nếu có)
└── Program.cs
```

---

## 4. Coding Conventions

### Naming

- **File & class**: PascalCase. Record, class, interface đều PascalCase.
- **Command**: `<Action><Entity>Command` — VD: `LoginUserCommand`, `CreateOrderCommand`
- **Query**: `Get<Entity>By<Field>Query` — VD: `GetUserByIdQuery`
- **Handler**: tên Command/Query + `Handler` suffix — VD: `LoginUserCommandHandler`
- **Validator**: tên Command/Query + `Validator` suffix
- **Integration Event**: `<Entity><Action>IntegrationEvent` — VD: `OrderCreatedIntegrationEvent`
- **Domain Event**: `<Entity><Action>DomainEvent` — VD: `UserRegisteredDomainEvent`
- **Repository interface**: `I<Entity>Repository` — VD: `IUserRepository`
- **Error constants**: static class `<Entity>Errors` hoặc `Error` record với `NotFound`, `Conflict`, v.v.
- **Permission strings**: `HdosPermissions.<Module><Action>` dạng `"module:action"` — VD: `"orders:create"`

### Record vs Class

- Commands, Queries, DTOs, Integration Events → `sealed record`
- Domain Entities, AggregateRoot → `class` (kế thừa base)
- Value Objects → `class` kế thừa `ValueObject`

### Kết quả trả về

Tất cả handler đều trả `Result<T>` hoặc `Result` (không ném exception vào application layer).

```csharp
// Đúng
public async Task<Result<LoginResultDto>> Handle(LoginUserCommand request, CancellationToken ct)

// Sai
public async Task<LoginResultDto> Handle(...) // ném exception thay vì Result
```

### Dependency Injection

- Đăng ký DI qua extension method `AddXxxApplication()`, `AddXxxInfrastructure()` trong từng layer.
- Không dùng `new` trực tiếp trong constructor; tất cả qua DI.

### Entity Factory

Domain entities được tạo qua **static factory method** `Create(...)`, không expose constructor public.

```csharp
public static User Create(Email email, string fullName, string passwordHash)
{
    var user = new User { ... };
    user.RaiseDomainEvent(new UserRegisteredDomainEvent(...));
    return user;
}
```

### Validation

FluentValidation trong Application layer. Mỗi Command/Query có Validator riêng.

---

## 5. Rules Khi Generate Code Mới

1. **Luôn follow pattern đang có**: xem file tương tự trong cùng service trước khi viết. Không tự dùng pattern mới (VD: không dùng REPR nếu codebase đang dùng Controller thuần).

2. **Đặt file đúng thư mục**: Command → `Features/<Action>/`, Entity → `Domain/Entities/`, Repository impl → `Infrastructure/Persistence/Repositories/`.

3. **Không sửa file ngoài phạm vi task**: nếu task là "thêm command X", chỉ sửa/tạo file liên quan đến X. Không refactor code xung quanh.

4. **Hỏi xác nhận trước khi sửa hơn 3 file cùng lúc** — trừ khi user đã nói rõ.

5. **Không xóa code cũ** nếu không được yêu cầu rõ ràng.

6. **Không thêm error handling cho scenario không thể xảy ra** — tin tưởng framework và internal code.

7. **Không thêm comment giải thích "WHAT"** — chỉ comment khi "WHY" không hiển nhiên.

8. **Mỗi service mới phải có**:
   - 4 project: `.Domain`, `.Application`, `.Infrastructure`, `.API`
   - Registration extension methods (`AddXxxApplication`, `AddXxxInfrastructure`)
   - EF Core DbContext + migration
   - Swagger config qua `UseHdosSwaggerUi`
   - Middleware qua `UseHdosMiddleware`
   - Monitoring qua `UseHdosMonitoring`

9. **Mỗi feature mới phải có doc** — tạo file `docs/NN-<ten-feature>.md` (xem docs/ để lấy số thứ tự tiếp theo).

---

## 6. Inter-Service Communication

### Synchronous — gRPC

Dùng cho call cần kết quả ngay (blocking).

```
OrderService ──gRPC──► AuthService:8081
  Mục đích: verify user tồn tại trước khi tạo order
  Protocol: HTTP/2 + Protobuf
  Contract: src/BuildingBlocks/Contracts/Protos/users.proto
  Client adapter: OrderService.Infrastructure/Grpc/AuthUserLookupClient.cs
```

AuthService lắng nghe trên port **8081** (HTTP/2 only). Các service khác dùng port **8080** (HTTP/1.1 + HTTP/2).

### Asynchronous — RabbitMQ (MassTransit)

Dùng cho event-driven, không cần kết quả ngay.

```
Publisher                   RabbitMQ exchange         Consumer(s)
OrderService          ──►  OrderCreatedIntegration  ──►  NotificationService
AuthService           ──►  UserRegisteredIntegration ──►  NotificationService
AsyncGateway          ──►  OrderCreateRequested      ──►  OrderService
DataMatchingService   ──►  DashboardFeReady          ──►  NotificationService
```

Tất cả event contracts định nghĩa trong `src/BuildingBlocks/Contracts/IntegrationEvents/`.

`IEventBus` (abstraction) → `MassTransitEventBus` (implementation, trong Common).

**Outbox Pattern** (OrderService): MassTransit EF Core Outbox đảm bảo at-least-once delivery.

### HTTP (AsyncGateway pattern)

```
Client POST /async/orders
  → AsyncGateway: lấy CustomerId từ JWT, publish OrderCreateRequestedIntegrationEvent
  → 202 Accepted + { correlationId }
  → OrderService consume từ queue, xử lý bất đồng bộ
```

### Không dùng

- HTTP call trực tiếp giữa các service (ngoài AsyncGateway)
- Shared database giữa hai service

---

## 7. Infrastructure — Chạy Local

### Docker network & hostname

Tất cả service nói chuyện với nhau qua hostname = tên service trong docker-compose (network `hdos-net`).

| Service | Hostname trong Docker | REST port | gRPC port |
|---------|----------------------|-----------|-----------|
| AuthService | `authservice` | 8080 | 8081 |
| OrderService | `orderservice` | 8080 | — |
| NotificationService | `notificationservice` | 8080 | — |
| M01Service | `m01service` | 8080 | — |
| DynamicFormService | `dynamicformservice` | 8080 | — |
| DataMatchingService | `datamatchingservice` | 8080 | — |
| AsyncGateway | `asyncgateway` | 8080 | — |

Từ host machine, truy cập qua **nginx**:
- HTTP: `http://localhost:5000/<prefix>/...`
- HTTPS: `https://localhost:8443/<prefix>/...`
- Swagger: `http://localhost:5000/<prefix>/swagger`

### Databases

| Service | DB name | Engine | Connection string key | Host (Docker) | Port (host) |
|---------|---------|--------|----------------------|---------------|-------------|
| AuthService | AuthDb | SQL Server | `ConnectionStrings__AuthDb` | `sqlserver:1433` | 1433 |
| OrderService | OrderDb | SQL Server | `ConnectionStrings__OrderDb` | `sqlserver:1433` | 1433 |
| NotificationService | NotificationDb | SQL Server | `ConnectionStrings__NotificationDb` | `sqlserver:1433` | 1433 |
| M01Service | M01Db | SQL Server | `ConnectionStrings__M01Db` | `sqlserver:1433` | 1433 |
| DynamicFormService | DynamicFormDb | PostgreSQL | `ConnectionStrings__DynamicFormDb` | `postgres-df:5432` | **5434** |
| DataMatchingService | DataMatchingDb | PostgreSQL | `ConnectionStrings__DataMatchingDb` | `postgres-dm:5432` | **5433** |

### Message broker & JWT

```
RabbitMQ AMQP : rabbitmq:5672  (host: localhost:5672)
RabbitMQ UI   : http://localhost:15672  (guest/guest)
Env vars      : RabbitMq__Host, RabbitMq__Port

JWT (tất cả service):
  Jwt__Issuer         = "hdos-auth"
  Jwt__Audience       = "hdos-api"
  Jwt__Secret         = <>=32 chars>
  Jwt__ExpiresMinutes = 480  (chỉ AuthService set)

gRPC OrderService→AuthService:
  Services__Auth__GrpcUrl = "http://authservice:8081"
```

### Lệnh thường dùng

```bash
# Khởi động full stack
docker compose up -d

# Rebuild một service cụ thể
docker compose up -d --build authservice

# Xem logs
docker compose logs -f authservice

# Chạy EF migration (ví dụ AuthService)
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
dotnet ef database update \
  --project src/Services/AuthService/AuthService.Infrastructure \
  --startup-project src/Services/AuthService/AuthService.API

# Build một service
dotnet build src/Services/AuthService/AuthService.API

# Test
dotnet test tests/AuthService.Tests

# Monitoring overlay
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d
# Grafana: http://localhost:3030 | Prometheus: 9090 | Loki: 3100 | Tempo: 3200
```

---

## 8. Những Thứ Cấm (Anti-Patterns)

### Kiến trúc

- **Không chia sẻ database** giữa hai service — mỗi service có DB riêng (Database-per-Service).
- **Không gọi HTTP trực tiếp** từ service này sang service khác (chỉ dùng gRPC hoặc RabbitMQ).
- **Không import project** của service này vào service khác — giao tiếp chỉ qua Contracts/BuildingBlocks.
- **Không đặt business logic** trong Controller hay Infrastructure layer.

### Code

- **Không ném exception** trong Application layer để biểu diễn lỗi nghiệp vụ — dùng `Result.Failure(Error...)`.
- **Không dùng `new` để tạo Entity** — phải qua static factory method `Create(...)`.
- **Không expose setter public** trên Entity/ValueObject.
- **Không đặt domain logic trong DbContext** hay Repository.
- **Không viết raw SQL** trừ khi cực kỳ cần thiết (đã thảo luận với team).
- **Không bỏ qua FluentValidation** cho Command/Query — mỗi request phải có Validator.
- **Không đặt connection string / secret** trong code — dùng environment variable.

### Messaging

- **Không tạo IntegrationEvent mới** mà không thêm vào `Contracts` project.
- **Không đặt consumer** trong Application layer — consumer thuộc Infrastructure.
- **Không publish event** mà không có consumer đang xử lý nó (dead letter).

### Testing

- **Không viết integration test** kết nối DB thật trong CI (chỉ unit test với NSubstitute).
- **Không bỏ qua test** khi thêm Command/Query handler mới.

---

## 9. Session Protocol (Bắt Buộc Với Claude)

> Claude phải enforce protocol này trong MỌI task coding. Đây là gate bắt buộc — không có ngoại lệ.

### Bước 1 — Gate: 3 Câu Trước Khi Viết Code

Trước khi bắt đầu bất kỳ task nào, Claude kiểm tra user đã cung cấp đủ 3 thứ:

| | Câu hỏi | Ví dụ đúng |
|--|---------|-----------|
| **WHAT** | Cụ thể làm gì? | *"Thêm `DeleteFormTemplateCommand` vào DynamicFormService"* |
| **CONSTRAINT** | Không được sửa gì? | *"Không sửa FormModule, FormField"* |
| **DONE WHEN** | Khi nào coi là xong? | *"Command + Handler + Validator. Build pass."* |

Nếu thiếu bất kỳ câu nào → Claude hỏi ngay, **không tự suy luận, không bắt đầu code**.

### Bước 2 — Plan: Khai Báo Scope Trước Execute

Sau khi đủ thông tin, Claude liệt kê và **chờ user xác nhận** trước khi code:

```
Files TẠO MỚI: [danh sách đường dẫn đầy đủ]
Files SỬA:     [danh sách đường dẫn đầy đủ]
Files KHÔNG SỬA: [nếu user khai báo]
```

Claude không được viết code trước khi user nói "OK", "đúng rồi", hoặc tương đương.

### Bước 3 — Execute: Scope Guard

Nếu trong khi viết code phát hiện cần thay đổi file ngoài danh sách đã approve:

- **DỪNG ngay** — không tự sửa
- Báo cáo file cần thêm và lý do
- Chờ user approve trước khi tiếp tục

### Bước 4 — Sau Execute: Nhắc Checklist

Sau mỗi session có thay đổi code, Claude nhắc user chạy:

```
[ ] git diff --name-only — file thay đổi có đúng scope không?
[ ] dotnet build — pass không có warning mới?
[ ] dotnet test — test hiện có vẫn pass?
[ ] Feature mới → đã tạo docs/NN-*.md? (xem docs/ để lấy số tiếp theo)
[ ] IntegrationEvent mới → đã thêm vào Contracts project?
[ ] Không có connection string / secret nào trong code?
```
