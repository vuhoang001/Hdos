# 02 — Cấu trúc dự án

## Layout thư mục gốc

```
Hdos/
├── src/
│   ├── BuildingBlocks/          ← Thư viện dùng chung
│   │   ├── SharedKernel/        ← Domain primitives (Entity, ValueObject...)
│   │   ├── Contracts/           ← Shared contracts (IntegrationEvents, .proto)
│   │   └── Common/              ← Cross-cutting concerns (logging, middleware...)
│   └── Services/
│       ├── AuthService/
│       ├── OrderService/
│       ├── NotificationService/
│       └── M01Service/
├── nginx/
│   └── nginx.conf               ← API Gateway config
├── monitoring/
│   ├── prometheus.yml
│   ├── loki.yml
│   ├── tempo.yml
│   └── grafana/provisioning/    ← Auto-provision datasources + dashboards
├── docs/                        ← Bộ tài liệu này
├── .github/
│   ├── workflows/
│   │   ├── ci.yml               ← Build, test, push image
│   │   └── cd.yml               ← Deploy to server
│   └── path-filters.yml         ← Detect which service changed
├── docker-compose.yml           ← Local development
├── docker-compose.server.yml    ← Production overlay
├── docker-compose.monitoring.yml← Monitoring overlay
├── services.json                ← CI/CD: service → Dockerfile mapping
└── Hdos.sln
```

---

## Cấu trúc mỗi Service

Tất cả services đều theo cùng một pattern Clean Architecture 4 layer:

```
AuthService/
├── AuthService.Domain/
│   ├── Entities/           ← AggregateRoot, Entity
│   ├── ValueObjects/       ← Email, Password (immutable)
│   ├── Events/             ← DomainEvent (in-process)
│   ├── Errors/             ← Domain error definitions
│   └── Repositories/       ← Interface (IUserRepository)
│
├── AuthService.Application/
│   ├── Features/
│   │   ├── Login/
│   │   │   ├── LoginUserCommand.cs
│   │   │   ├── LoginUserCommandHandler.cs
│   │   │   └── LoginUserCommandValidator.cs
│   │   ├── Register/
│   │   └── GetUser/
│   ├── DTOs/               ← Request/Response models
│   └── EventHandlers/      ← Handle DomainEvents, publish IntegrationEvents
│
├── AuthService.Infrastructure/
│   ├── Persistence/
│   │   ├── AuthDbContext.cs
│   │   ├── Repositories/   ← EF Core implementation
│   │   └── Migrations/
│   └── DependencyInjection.cs
│
└── AuthService.API/
    ├── Controllers/
    ├── Grpc/               ← gRPC service impl (AuthService only)
    ├── appsettings.json
    ├── appsettings.Production.json
    └── Program.cs
```

**Tại sao chia 4 project?** Dependency rule: Domain không import gì cả. Application chỉ import Domain. Infrastructure import Application (implement interfaces). API import tất cả để wire up. Compiler enforce điều này — nếu ai cố import Infrastructure vào Domain sẽ build fail.

---

## BuildingBlocks

### SharedKernel
Các primitive của DDD dùng chung — không chứa business logic:

```
SharedKernel/
├── BaseEntity.cs           ← Id (Guid), DomainEvents list
├── AggregateRoot.cs        ← Kế thừa BaseEntity
├── IDomainEvent.cs         ← Marker interface
├── ValueObject.cs          ← Abstract, equality by value
└── Result.cs               ← Result<T> pattern (không throw exception)
```

### Contracts
Shared contracts giữa services — là "ngôn ngữ chung":

```
Contracts/
├── IntegrationEvents/
│   ├── UserRegisteredIntegrationEvent.cs
│   ├── UserLoggedInIntegrationEvent.cs
│   └── OrderCreatedIntegrationEvent.cs
└── Grpc/
    └── users.proto         ← gRPC service definition
```

**Quan trọng:** Khi publisher thay đổi field trong IntegrationEvent, mọi consumer phải cập nhật. Đây là breaking change — cần coordinate.

### Common
Tất cả cross-cutting concerns:

```
Common/
├── Auth/
│   ├── JwtAuthExtensions.cs   ← AddHdosJwtAuth()
│   └── JwtTokenIssuer.cs      ← Tạo JWT (AuthService only)
├── Extensions/
│   ├── ServiceCollectionExtensions.cs  ← AddHdosCors()
│   └── WebApplicationExtensions.cs    ← UseHdosMiddleware(), UseHdosMonitoring()
├── HealthChecks/
│   └── HealthCheckExtensions.cs  ← AddHdosHealthChecks(), MapHdosHealthChecks()
├── Logging/
│   └── LoggingExtensions.cs   ← UseHdosLogging() (Serilog + Loki sink)
├── Messaging/
│   ├── RabbitMqEventBus.cs    ← IEventBus implementation
│   └── RabbitMqConsumerHostedService.cs  ← Base class cho consumers
├── Middleware/
│   ├── ExceptionHandlingMiddleware.cs
│   └── RequestLoggingMiddleware.cs
├── Monitoring/
│   └── OpenTelemetryExtensions.cs  ← AddHdosOpenTelemetry()
├── Persistence/
│   └── PublishDomainEventsInterceptor.cs  ← EF Core hook
└── Responses/
    └── ApiResponse.cs         ← Unified response wrapper
```

---

## Convention đặt tên

| Loại | Convention | Ví dụ |
|------|-----------|-------|
| Command | `{Verb}{Noun}Command` | `RegisterUserCommand` |
| Query | `Get{Noun}{Filter}Query` | `GetUserByIdQuery` |
| Handler | `{Command/Query}Handler` | `RegisterUserCommandHandler` |
| Domain Event | `{Noun}{PastTense}DomainEvent` | `UserRegisteredDomainEvent` |
| Integration Event | `{Noun}{PastTense}IntegrationEvent` | `UserRegisteredIntegrationEvent` |
| Consumer | `{EventName}Consumer` | `UserRegisteredConsumer` |
| Repository interface | `I{Noun}Repository` | `IUserRepository` |
| Value Object | Noun | `Email`, `Money` |

---

## Quy tắc dependency

```
API → Application → Domain
 ↘         ↓
Infrastructure → Application
```

- Domain: **không import gì** ngoài .NET BCL
- Application: chỉ import Domain + SharedKernel
- Infrastructure: import Application (để implement interfaces)
- API: import tất cả (DI wiring)

Nếu vi phạm → build fail. Đây là cơ chế enforce architecture.
