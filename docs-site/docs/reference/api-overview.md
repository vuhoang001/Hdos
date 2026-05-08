---
title: API Reference — Overview
sidebar_position: 1
description: Liệt kê toàn bộ endpoint REST + gRPC + integration event của hệ thống.
tags: [reference, api]
---

# API Reference — Overview

> **Loại:** Reference · **Mục đích:** Tra cứu, không kể chuyện.

## REST Endpoints (qua Gateway `:5000`)

### AuthService (`/auth/*`)

| Method | Path | Auth | Use case |
|---|---|---|---|
| POST | `/auth/register` | Anonymous | Đăng ký user |
| POST | `/auth/login` | Anonymous | Đăng nhập, trả JWT |
| GET | `/auth/users/{id}` | `[Authorize]` | Lấy user theo id |
| GET | `/auth/health` | Anonymous | Health check |

### OrderService (`/orders/*`)

| Method | Path | Auth | Use case |
|---|---|---|---|
| POST | `/orders` | `[Authorize]` | Tạo order (gọi gRPC verify user) |
| GET | `/orders/{id}` | `[Authorize]` | Lấy order theo id |
| POST | `/orders/{id}/cancel` | `[Authorize]` | Hủy order |
| GET | `/orders/health` | Anonymous | Health check |

### NotificationService (`/notifications/*`)

| Method | Path | Auth | Use case |
|---|---|---|---|
| GET | `/notifications` | `[Authorize]` | List notification của user |
| WS | `/notifications/hubs/notifications` | `?access_token=...` | SignalR realtime |
| GET | `/notifications/health` | Anonymous | Health check |

## gRPC Services (port `:5111`)

| Service | Method | Caller | Mục đích |
|---|---|---|---|
| `UserService` | `GetUserById(UserIdRequest)` | OrderService | Lookup user khi tạo order |
| `UserService` | `UserExists(UserIdRequest)` | OrderService | Verify user tồn tại |

Hợp đồng: `src/BuildingBlocks/Contracts/Protos/users.proto`.

## Integration Events (qua RabbitMQ exchange `hdos.events`)

| Event | Publisher | Consumer | Routing key |
|---|---|---|---|
| `UserRegisteredIntegrationEvent` | AuthService | NotificationService | tên class |
| `UserLoggedInIntegrationEvent` | AuthService | NotificationService | tên class |
| `OrderCreatedIntegrationEvent` | OrderService | NotificationService | tên class |
| `OrderCancelledIntegrationEvent` | OrderService | NotificationService | tên class |

## Response wrapper

Tất cả REST endpoint trả về dạng:

```json
{
  "success": true,
  "data": { ... },
  "error": null,
  "traceId": "0HMVGT..."
}
```

Hoặc khi lỗi:

```json
{
  "success": false,
  "data": null,
  "error": { "code": "Conflict", "message": "Email đã tồn tại" },
  "traceId": "0HMVGT..."
}
```
