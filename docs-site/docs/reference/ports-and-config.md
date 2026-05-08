---
title: Ports & Configuration
sidebar_position: 3
description: Cổng mặc định và cấu hình env var.
tags: [reference, config, ports]
---

# Ports & Configuration

## Ports mặc định

| Service | REST | gRPC | DB | RabbitMQ |
|---|---|---|---|---|
| ApiGateway | 5000 | — | — | — |
| AuthService | 5101 | 5111 | AuthDb | publisher |
| OrderService | 5102 | — | OrderDb | publisher |
| NotificationService | 5103 | — | NotificationDb | consumer (3 queue) |
| SQL Server | 1433 | — | — | — |
| RabbitMQ | 5672 (AMQP), 15672 (UI) | — | — | — |

## Env var override

```yaml
environment:
  Kestrel__RestPort: 8080
  Kestrel__GrpcPort: 8081
  ConnectionStrings__AuthDb: "Server=sqlserver,1433;Database=AuthDb;..."
  RabbitMq__Host: rabbitmq
  Jwt__Secret: "..."
  Jwt__Issuer: "Hdos.Auth"
  Jwt__Audience: "Hdos.Services"
  Jwt__ExpiresMinutes: "60"
```

## Cấu hình JWT (share giữa Gateway + 3 service)

```json:src/Services/AuthService/AuthService.API/appsettings.json
"Jwt": {
  "Secret": "REPLACE_WITH_32+_CHAR_RANDOM_STRING",
  "Issuer": "Hdos.Auth",
  "Audience": "Hdos.Services",
  "ExpiresMinutes": 60
}
```

⚠️ `Secret` trong `appsettings.json` chỉ cho dev. Prod dùng env var hoặc Vault — xem [ADR-0001](../adr/0001-record-architecture-decisions).
