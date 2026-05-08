---
title: C4 Level 3 — Component (AuthService)
sidebar_position: 3
description: Bên trong AuthService — 4 tầng Clean Architecture, MediatR pipeline.
tags: [explanation, c4, architecture, diagram, clean-architecture]
---

# C4 Level 3 — Component (AuthService)

Zoom vào trong **AuthService** (từ level [Container](./container)). Mỗi "component" = 1 logical grouping của class (~ 1 folder hoặc 1 lớp có tên rõ ràng).

## Diagram

```mermaid
flowchart TB
    classDef component fill:#85bbf0,stroke:#5d82a8,color:#000
    classDef external fill:#999999,stroke:#6b6b6b,color:#fff
    classDef db fill:#438dd5,stroke:#2e6295,color:#fff
    classDef queue fill:#ff9800,stroke:#b36b00,color:#000

    Gateway["🌐 API Gateway"]:::external
    Order["🛒 OrderService"]:::external

    subgraph Auth["🔐 AuthService"]
        direction TB

        subgraph API["AuthService.API"]
            Ctrl["AuthController<br/><i>REST endpoints</i>"]:::component
            Grpc["UserGrpcService<br/><i>gRPC server</i>"]:::component
        end

        subgraph App["AuthService.Application"]
            Send["ISender (MediatR)"]:::component
            Pipeline["Pipeline:<br/>Logging → Validation"]:::component
            Handlers["Handlers<br/>RegisterUserCommand<br/>LoginUserCommand<br/>GetUserByIdQuery"]:::component
        end

        subgraph Domain["AuthService.Domain"]
            User["User<br/><i>AggregateRoot</i>"]:::component
            Email["Email<br/><i>ValueObject</i>"]:::component
            Events["Domain Events"]:::component
        end

        subgraph Infra["AuthService.Infrastructure"]
            Repo["UserRepository<br/><i>impl IUserRepository</i>"]:::component
            DbCtx["AuthDbContext<br/><i>EF Core</i>"]:::component
            Hasher["PasswordHasher<br/><i>BCrypt</i>"]:::component
            Bus["EventBus<br/><i>RabbitMQ</i>"]:::component
            Jwt["JwtTokenIssuer<br/><i>HS256</i>"]:::component
        end
    end

    AuthDb[("🗄️ AuthDb")]:::db
    Rabbit{{"📮 RabbitMQ"}}:::queue

    Gateway -->|HTTP| Ctrl
    Order -.->|gRPC| Grpc

    Ctrl --> Send
    Send --> Pipeline
    Pipeline --> Handlers
    Grpc -.->|"thẳng repo,<br/>bypass MediatR"| Repo

    Handlers --> User
    Handlers --> Repo
    Handlers --> Hasher
    Handlers --> Jwt
    Handlers --> Bus

    User --> Email
    User --> Events

    Repo --> DbCtx
    DbCtx --> AuthDb
    Bus -->|publish| Rabbit
```

## Chú thích

| Component | Tầng | Vai trò |
|---|---|---|
| `AuthController` | API | REST endpoint, dispatch qua `ISender` |
| `UserGrpcService` | API | gRPC server, gọi thẳng `IUserRepository` (bypass MediatR cho read tốc độ cao) |
| `Pipeline` | Application | `IPipelineBehavior`: log + validate trước khi vào Handler |
| `Handlers` | Application | 1 Command/Query = 1 Handler, return `Result<T>` |
| `User` | Domain | Aggregate, có factory `Register()` raise domain event |
| `UserRepository` | Infrastructure | Impl `IUserRepository` qua EF Core |
| `EventBus` | Infrastructure | Publish `IntegrationEvent` lên RabbitMQ |
| `JwtTokenIssuer` | Infrastructure | Ký JWT HS256, đọc `Jwt:Secret` |

## Quan sát thiết kế

- **Dependency direction**: Domain ← Application ← Infrastructure & API. Domain không reference component bên dưới.
- **gRPC bypass MediatR**: read-only call nhỏ, không cần Validation/Logging pipeline. Trade-off có document trong [ADR-0001](../../adr/0001-record-architecture-decisions).
- **Email là ValueObject**: format validate ở `Email.Create(...)`, không phải responsibility của Handler.
