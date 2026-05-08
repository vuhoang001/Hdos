---
title: C4 Level 2 — Container
sidebar_position: 2
description: Bên trong Hdos — Gateway, 3 service, DB, RabbitMQ.
tags: [explanation, c4, architecture, diagram]
---

# C4 Level 2 — Container

Zoom vào bên trong **Hdos Platform** (từ level [Context](./context)). Mỗi "container" = 1 deployable unit (một process runtime, một DB instance, một queue).

## Diagram

```mermaid
flowchart TB
    classDef person fill:#08427b,stroke:#052e56,color:#fff
    classDef container fill:#1168bd,stroke:#0b4884,color:#fff
    classDef db fill:#438dd5,stroke:#2e6295,color:#fff
    classDef queue fill:#ff9800,stroke:#b36b00,color:#000

    User["👤 End User"]:::person

    subgraph Hdos["📦 Hdos Platform"]
        Gateway["🌐 API Gateway<br/>YARP, .NET 8<br/><i>Port 5000</i>"]:::container

        Auth["🔐 AuthService<br/>.NET 8<br/><i>REST 5101, gRPC 5111</i>"]:::container
        Order["🛒 OrderService<br/>.NET 8<br/><i>REST 5102</i>"]:::container
        Notif["🔔 NotificationService<br/>.NET 8<br/><i>REST 5103, SignalR Hub</i>"]:::container

        AuthDb[("🗄️ AuthDb<br/>SQL Server")]:::db
        OrderDb[("🗄️ OrderDb<br/>SQL Server")]:::db
        NotifDb[("🗄️ NotificationDb<br/>SQL Server")]:::db

        Rabbit{{"📮 RabbitMQ<br/>exchange: hdos.events<br/>topic"}}:::queue
    end

    User -->|"HTTPS<br/>JWT Bearer"| Gateway

    Gateway -->|"/auth/*<br/>HTTP"| Auth
    Gateway -->|"/orders/*<br/>HTTP"| Order
    Gateway -->|"/notifications/*<br/>HTTP + WS"| Notif

    Order -.->|"GetUserById<br/>gRPC :5111"| Auth

    Auth --> AuthDb
    Order --> OrderDb
    Notif --> NotifDb

    Auth -->|"publish<br/>UserRegistered<br/>UserLoggedIn"| Rabbit
    Order -->|"publish<br/>OrderCreated<br/>OrderCancelled"| Rabbit
    Rabbit -->|"consume<br/>3 queue riêng"| Notif
```

## Chú thích

| Container | Tech | Trách nhiệm |
|---|---|---|
| API Gateway | YARP | Reverse proxy, áp `AuthorizationPolicy` per route |
| AuthService | .NET 8 | User CRUD, login, JWT issuer, gRPC user lookup server |
| OrderService | .NET 8 | Order CRUD, gọi gRPC verify user, publish `OrderCreated` |
| NotificationService | .NET 8 | Consume Rabbit, lưu noti, push realtime qua SignalR |
| 3 SQL Database | SQL Server | Tách DB per service — không share schema |
| RabbitMQ | RabbitMQ | Topic exchange `hdos.events`, mỗi consumer 1 queue riêng |

## Quyết định kiến trúc liên quan

- [ADR-0002: Tách port REST/gRPC](../../adr/0002-split-rest-grpc-ports)

## Kế tiếp

Zoom vào trong 1 service: [C4 Component — AuthService](./component-auth).
