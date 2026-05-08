---
title: How to add authentication cho 1 endpoint
sidebar_position: 1
description: Bật JWT bảo vệ cho 1 endpoint REST mới — gồm cả Gateway policy và controller attribute.
tags: [how-to, jwt, security]
---

# How-to — Bảo vệ endpoint bằng JWT

> **Loại:** How-to · **Pre-req:** Hiểu pipeline auth — xem [Explanation: Luồng request & auth](../explanation/why-clean-architecture)

Áp dụng khi bạn vừa tạo 1 endpoint REST mới và muốn nó **chỉ cho user đã login** gọi.

## 1. Gắn `[Authorize]` ở controller

```csharp:src/Services/OrderService/OrderService.API/Controllers/OrdersController.cs
[ApiController]
[Route("orders")]
[Authorize]                       // class-level → tất cả action thừa kế
public sealed class OrdersController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(...) { ... }

    [AllowAnonymous]              // override class-level cho endpoint công khai
    [HttpGet("health")]
    public IActionResult Health() => Ok();
}
```

## 2. Gateway: route phải dùng policy `Default`

```json:src/ApiGateway/appsettings.json
"orders-route": {
  "ClusterId": "orders-cluster",
  "AuthorizationPolicy": "Default",   // = RequireAuthenticatedUser
  "Match": { "Path": "/orders/{**catch-all}" }
}
```

| Policy | Hành vi |
|---|---|
| `Default` | Phải có JWT hợp lệ. Sai/thiếu → 401 ngay tại Gateway. |
| `Anonymous` | Bỏ qua kiểm — dùng cho `/auth/*`, `/health`, `/swagger/*`. |

## 3. Đọc `userId` trong action

```csharp
var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
var email  = User.FindFirstValue(ClaimTypes.Email);
```

> JWT mặc định remap `sub` → `ClaimTypes.NameIdentifier`. Muốn dùng đúng tên claim gốc, set `JwtSecurityTokenHandler.DefaultMapInboundClaims = false` trong `Program.cs`.

## 4. Verify

```bash
# Không có token → 401
curl -i http://localhost:5000/orders

# Có token hợp lệ → 200/201
curl -i http://localhost:5000/orders -H "Authorization: Bearer $TOKEN"
```

## Liên quan

- [Debug 401](./debug-401)
- [ADR-0002: Tách port REST/gRPC](../adr/0002-split-rest-grpc-ports)
