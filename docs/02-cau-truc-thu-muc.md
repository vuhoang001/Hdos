# 02 — Cấu trúc thư mục

## 1. Cây thư mục đầy đủ

```
Hdos/
├── Hdos.sln                              # Solution chứa tất cả project
├── docker-compose.yml                    # SQL Server + RabbitMQ + 3 service + Gateway
├── README.md                             # Quick-start (tiếng Anh)
├── docs/                                 # Tài liệu chi tiết (tiếng Việt) — bạn đang đọc
└── src/
    ├── ApiGateway/                       # YARP reverse proxy
    │   ├── Program.cs
    │   ├── appsettings.json              # Routes + Clusters cho local
    │   ├── appsettings.Docker.json       # Routes + Clusters cho docker (hostname container)
    │   └── Dockerfile
    │
    ├── BuildingBlocks/                   # Code dùng chung — không reference Service
    │   ├── SharedKernel/                 # DDD primitives
    │   │   ├── BaseEntity.cs
    │   │   ├── AggregateRoot.cs
    │   │   ├── ValueObject.cs
    │   │   ├── IDomainEvent.cs
    │   │   └── Result.cs
    │   ├── Contracts/                    # Hợp đồng giữa các service
    │   │   ├── IntegrationEvents/        #  - Class event đi qua RabbitMQ
    │   │   └── Protos/                   #  - File .proto cho gRPC
    │   │       └── users.proto
    │   └── Common/                       # Cross-cutting
    │       ├── Behaviors/                # MediatR pipeline
    │       ├── Exceptions/
    │       ├── Extensions/
    │       ├── Logging/
    │       ├── Messaging/                # IEventBus + RabbitMQ implementation
    │       ├── Middleware/
    │       └── Responses/
    │
    └── Services/
        ├── AuthService/
        │   ├── AuthService.Domain/
        │   │   ├── Entities/             # User
        │   │   ├── ValueObjects/         # Email
        │   │   ├── Events/               # UserRegisteredDomainEvent
        │   │   └── Repositories/         # IUserRepository, IUnitOfWork
        │   ├── AuthService.Application/
        │   │   ├── Abstractions/         # IPasswordHasher
        │   │   ├── DTOs/                 # UserDto, LoginResultDto
        │   │   ├── Features/             # CQRS use case theo folder
        │   │   │   ├── Register/
        │   │   │   ├── Login/
        │   │   │   └── GetUser/
        │   │   └── DependencyInjection.cs
        │   ├── AuthService.Infrastructure/
        │   │   ├── Persistence/          # AuthDbContext, UserRepository, Migrations
        │   │   ├── Security/             # PasswordHasher (BCrypt)
        │   │   └── DependencyInjection.cs
        │   └── AuthService.API/
        │       ├── Controllers/          # AuthController (REST)
        │       ├── Grpc/                 # UserGrpcService (gRPC server)
        │       ├── Program.cs            # Kestrel 2 cổng + DI compose
        │       └── Dockerfile
        │
        ├── OrderService/                 # Cấu trúc tương tự Auth
        │   ├── OrderService.Domain/      # Order (aggregate), OrderItem, Money
        │   ├── OrderService.Application/
        │   │   ├── Abstractions/         # IUserLookupService (gRPC port từ Application)
        │   │   ├── Features/CreateOrder, GetOrder
        │   ├── OrderService.Infrastructure/
        │   │   └── Grpc/                 # AuthUserLookupClient (impl IUserLookupService)
        │   └── OrderService.API/
        │
        └── NotificationService/
            ├── NotificationService.Domain/      # Notification entity + repo
            ├── NotificationService.Application/
            │   ├── EventHandlers/        # IIntegrationEventHandler<TEvent> implementations
            │   └── Features/ListNotifications/
            ├── NotificationService.Infrastructure/
            │   └── Consumers/            # RabbitMqConsumerHostedService<TEvent, THandler>
            └── NotificationService.API/
```

## 2. Project reference graph

Mũi tên `A → B` đọc là *"A reference B"*.

```
                 ┌──────────────┐
                 │ SharedKernel │ ← không reference ai
                 └─────▲────────┘
                       │
       ┌───────────────┼───────────────┐
       │               │               │
 ┌─────┴────┐   ┌──────┴─────┐   ┌─────┴────┐
 │ Domain   │   │ Domain     │   │ Domain   │   (3 service)
 │ (Auth)   │   │ (Order)    │   │ (Notif)  │
 └─────▲────┘   └──────▲─────┘   └─────▲────┘
       │               │               │
 ┌─────┴────┐   ┌──────┴─────┐   ┌─────┴────────┐
 │Application│  │Application │   │ Application  │
 └─────▲────┘   └──────▲─────┘   └─────▲────────┘
       │               │               │
 ┌─────┴───────┐ ┌─────┴───────┐ ┌─────┴───────┐
 │Infrastructure│ │Infrastructure│ │Infrastructure│
 └─────▲───────┘ └─────▲───────┘ └─────▲───────┘
       │               │               │
       │               │               │
       │ ┌─────────────┼───────────────┘
       │ │             │
       │ │       ┌─────┴────────┐
       └─┴───────┤   *.API      │
                 └──────┬───────┘
                        │
                ┌───────▼─────────┐    ┌──────────────┐
                │ BuildingBlocks/ │◄───┤ Contracts +  │
                │ Common          │    │ SharedKernel │
                └─────────────────┘    └──────────────┘

ApiGateway → (chỉ Microsoft.* + YARP), không reference service nào.
```

## 3. Quy ước đặt namespace & assembly

| Project                                | Namespace                                | Assembly                                |
|----------------------------------------|------------------------------------------|------------------------------------------|
| `SharedKernel`                         | `Hdos.SharedKernel`                      | `Hdos.SharedKernel`                      |
| `Contracts`                            | `Hdos.Contracts.*`                       | `Hdos.Contracts`                         |
| `Common`                               | `Hdos.Common.*`                          | `Hdos.Common`                            |
| `AuthService.Domain`                   | `Hdos.AuthService.Domain.*`              | `Hdos.AuthService.Domain`                |
| `AuthService.Application`              | `Hdos.AuthService.Application.*`         | `Hdos.AuthService.Application`           |
| `AuthService.Infrastructure`           | `Hdos.AuthService.Infrastructure.*`      | `Hdos.AuthService.Infrastructure`        |
| `AuthService.API`                      | `Hdos.AuthService.API.*`                 | `Hdos.AuthService.API`                   |
| (tương tự cho `OrderService`, `NotificationService`)                                                              |

Lý do: prefix `Hdos.` tránh đụng package từ NuGet, hậu tố theo lớp giúp đọc
stack trace là biết ngay code thuộc service nào và tầng nào.

## 4. Folder *Features* trong Application

Mỗi use case nằm trong **một folder riêng**, mỗi folder có thể chứa:

```
Features/
└── CreateOrder/
    ├── CreateOrderCommand.cs        # record Command + Validator + Handler trong cùng 1 file
    ├── CreateOrderEndpoint.cs       # (tùy chọn) nếu dùng Minimal API
    └── …
```

Ưu điểm:

- Đụng feature nào chỉ cần mở **một folder**.
- Add feature mới = tạo folder mới + 1 file, không phải sửa rải rác trong các
  folder Commands/Queries/Handlers.
- Test theo feature dễ co-locate.

## 5. Test project (chưa có sẵn — placeholder)

Cấu trúc đề xuất khi thêm test:

```
tests/
├── AuthService.UnitTests/
├── AuthService.IntegrationTests/
├── OrderService.UnitTests/
└── …
```

Reference từ test → service: chỉ test `Application` và `Domain` thuần.
Integration test mới chạm `Infrastructure` (DB thật / RabbitMQ test container).
