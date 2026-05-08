# Hdos — .NET 8 Microservices Demo

A clean-architecture, DDD-style microservices demo built with .NET 8, YARP, RabbitMQ, SQL Server, EF Core and MediatR.

```
Client ──► ApiGateway (YARP)
              │
              ├──► AuthService          ──► AuthDb (SQL)
              ├──► OrderService         ──► OrderDb (SQL)
              └──► NotificationService  ──► NotificationDb (SQL)

           AuthService  ──publish──► RabbitMQ ──consume──► NotificationService
           OrderService ──publish──┘
```

## 1. Architecture overview

### High-level

- **ApiGateway** — single entry point. YARP reverse proxy forwards `/auth/*`, `/orders/*`, `/notifications/*` to the right service. Adds request logging.
- **AuthService** — owns users, login, register. Publishes `UserRegistered`, `UserLoggedIn` integration events.
- **OrderService** — owns orders. Publishes `OrderCreated` integration event.
- **NotificationService** — consumes integration events from RabbitMQ and persists Notifications. Has a query API to inspect what was sent.
- **BuildingBlocks** — shared libraries: `SharedKernel`, `Contracts`, `Common`.

### Communication

- **Sync** — HTTP through the gateway (`http://localhost:5000`).
- **Async** — RabbitMQ `topic` exchange `hdos.events`. Routing key = event type name. Each consumer has its own queue.

### Clean Architecture per service

```
Service/
├── *.Domain          ← entities, aggregates, value objects, domain events, repository interfaces
├── *.Application     ← CQRS (MediatR), validators, DTOs, use-case orchestration
├── *.Infrastructure  ← EF Core DbContext, repositories, RabbitMQ consumers, external integrations
└── *.API             ← Controllers, Program.cs, middleware
```

Dependency rule (inward only):

```
API ──► Application ──► Domain
 │           │
 └──► Infrastructure ──► Application ──► Domain
```

`Domain` references **only** `SharedKernel`. `Application` references `Domain`, `Common`, `Contracts`. `Infrastructure` implements `Application`/`Domain` interfaces and may bring EF/RabbitMQ. `API` wires everything up.

## 2. Solution layout

```
Hdos/
├── Hdos.sln
├── docker-compose.yml
├── README.md
└── src/
    ├── ApiGateway/                                  # YARP
    ├── BuildingBlocks/
    │   ├── SharedKernel/                            # BaseEntity, AggregateRoot, Result, ValueObject, IDomainEvent
    │   ├── Contracts/                               # Integration events (cross-service contracts)
    │   └── Common/                                  # Middleware, behaviors, RabbitMQ event bus, Serilog config
    └── Services/
        ├── AuthService/{Domain,Application,Infrastructure,API}
        ├── OrderService/{Domain,Application,Infrastructure,API}
        └── NotificationService/{Domain,Application,Infrastructure,API}
```

## 3. Building blocks

### SharedKernel
- `BaseEntity<TId>` — identity + auditing.
- `AggregateRoot<TId>` — adds `DomainEvents` collection and `RaiseDomainEvent`.
- `IDomainEvent` / `DomainEvent` — MediatR `INotification` based.
- `ValueObject` — equality by components.
- `Result` / `Result<T>` / `Error` — Result pattern.

### Contracts
- `IntegrationEvent` base.
- `UserRegisteredIntegrationEvent`, `UserLoggedInIntegrationEvent`, `OrderCreatedIntegrationEvent`.
- These are the only DTO contracts shared across service boundaries.

### Common
- `ExceptionHandlingMiddleware` — maps exceptions to JSON `ApiResponse` with proper status codes.
- `RequestLoggingMiddleware` — logs every HTTP call with elapsed ms.
- `ApiResponse` / `ApiResponse<T>` — standard response wrapper.
- `LoggingBehavior`, `ValidationBehavior` — MediatR pipelines.
- `IEventBus` + `RabbitMqEventBus` + `RabbitMqConnection` — durable topic exchange publisher with retry-on-connect.
- `RabbitMqConsumerHostedService<TEvent, THandler>` — generic background consumer (auto-declare exchange/queue/binding, manual ack/nack with re-queue once).
- `SerilogConfig.UseHdosLogging` — preconfigured console logging tagged with the service name.

## 4. CQRS + Domain example walkthrough

### `RegisterUserCommand` (Auth)

1. `POST /auth/register` lands on `AuthController.Register` (in `AuthService.API`).
2. `ISender.Send(cmd)` dispatches through MediatR → `LoggingBehavior` → `ValidationBehavior` (FluentValidation) → `RegisterUserCommandHandler`.
3. Handler calls `Email.Create()` (value object), checks uniqueness via `IUserRepository.ExistsByEmailAsync`, creates the `User` aggregate (raises `UserRegisteredDomainEvent` internally).
4. `IUnitOfWork.SaveChangesAsync()` commits via EF Core.
5. `IEventBus.PublishAsync(new UserRegisteredIntegrationEvent(...))` → RabbitMQ.
6. Returns `ApiResponse<UserDto>` to caller.

### Async flow — `OrderCreated` → `NotificationService`

1. `OrderService` publishes `OrderCreatedIntegrationEvent` to exchange `hdos.events` with routing key `OrderCreatedIntegrationEvent`.
2. `OrderCreatedConsumer` (HostedService in `NotificationService.Infrastructure`) is bound to queue `notification.order-created`, deserializes the message, opens a DI scope, resolves `OrderCreatedEventHandler`, persists a `Notification` row.
3. Failure → `BasicNack` (re-queue once on first failure, then drop to avoid poison-pill loop).

## 5. Endpoints

| Path through gateway              | Forwarded to        | Description                        |
| --------------------------------- | ------------------- | ---------------------------------- |
| `POST /auth/register`             | AuthService         | Register a user                    |
| `POST /auth/login`                | AuthService         | Login (returns demo token)         |
| `GET  /auth/users/{id}`           | AuthService         | Get user by id                     |
| `POST /orders`                    | OrderService        | Create an order                    |
| `GET  /orders/{id}`               | OrderService        | Get order by id                    |
| `GET  /notifications`             | NotificationService | List recent notifications          |
| `GET  /<service>/health`          | each service        | Health check                       |
| `GET  /` and `/health` (gateway)  | ApiGateway          | Gateway info & health              |

Direct service ports (when running locally without docker):

- ApiGateway: `5000`
- AuthService: `5101`
- OrderService: `5102`
- NotificationService: `5103`
- SQL Server: `1433` (sa / `Hdos!Pass123`)
- RabbitMQ AMQP: `5672`, management UI: `15672` (guest / guest)

## 6. Running with Docker (recommended)

```bash
cd Hdos
docker compose up --build
```

Then:
```bash
# Health
curl http://localhost:5000/health
curl http://localhost:5000/auth/health
curl http://localhost:5000/orders/health
curl http://localhost:5000/notifications/health

# Register a user
curl -X POST http://localhost:5000/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"alice@hdos.io","fullName":"Alice","password":"secret123"}'

# Login (publishes UserLoggedIn event consumed by NotificationService)
curl -X POST http://localhost:5000/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"alice@hdos.io","password":"secret123"}'

# Create an order (publishes OrderCreated event)
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId":"00000000-0000-0000-0000-000000000001",
    "customerEmail":"alice@hdos.io",
    "items":[{"productName":"Book","quantity":2,"unitPrice":15.50,"currency":"USD"}]
  }'

# Inspect notifications produced by event handlers
curl http://localhost:5000/notifications
```

RabbitMQ UI: `http://localhost:15672` (guest / guest) — you should see exchange `hdos.events` and the three notification queues.

## 7. Running locally without Docker

You still need SQL Server and RabbitMQ. Easiest:

```bash
docker compose up -d sqlserver rabbitmq
```

Then run each service in its own terminal (requires .NET 8 SDK):

```bash
dotnet run --project src/Services/AuthService/AuthService.API
dotnet run --project src/Services/OrderService/OrderService.API
dotnet run --project src/Services/NotificationService/NotificationService.API
dotnet run --project src/ApiGateway
```

Each service auto-applies EF Core migrations on startup (`Database.MigrateAsync()` with retry).

## 8. Adding EF Core migrations

The first time you want a real migration, do this for each service (from the repo root):

```bash
dotnet tool install --global dotnet-ef     # one-time

dotnet ef migrations add Init \
  --project src/Services/AuthService/AuthService.Infrastructure \
  --startup-project src/Services/AuthService/AuthService.API \
  -o Persistence/Migrations

dotnet ef migrations add Init \
  --project src/Services/OrderService/OrderService.Infrastructure \
  --startup-project src/Services/OrderService/OrderService.API \
  -o Persistence/Migrations

dotnet ef migrations add Init \
  --project src/Services/NotificationService/NotificationService.Infrastructure \
  --startup-project src/Services/NotificationService/NotificationService.API \
  -o Persistence/Migrations
```

`MigrationsAssembly(...)` is already wired in each `DependencyInjection.cs`, so these migrations live next to their `DbContext`.

## 9. Adding a new microservice

1. `mkdir src/Services/PaymentService/{...Domain,...Application,...Infrastructure,...API}` and copy a service as a template.
2. Add `Hdos.Contracts` integration events for any cross-service messages.
3. Wire YARP route + cluster in `src/ApiGateway/appsettings.json` (and `appsettings.Docker.json` for the container hostname).
4. Add a service block in `docker-compose.yml`, mount its DB connection string, expose its port.
5. Reference projects from `Hdos.sln` (or `dotnet sln add`).

## 10. Tech stack

- .NET 8 / ASP.NET Core 8
- YARP `2.2.x`
- MediatR `12.4.x`
- FluentValidation `11.10.x`
- EF Core / EF Core SqlServer `8.0.x`
- RabbitMQ.Client `6.8.x`
- Serilog (console)
- Swashbuckle for Swagger UI on each service (Development only)

## 11. Next steps you might want to add

- Identity/JWT and authorization at the gateway.
- Outbox pattern for transactional event publishing.
- OpenTelemetry tracing across services.
- A real test project per service (the structure is ready for it).
- Health checks endpoint `/healthz` aggregated by the gateway.
