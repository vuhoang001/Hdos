---
title: Setup project Hdos local
sidebar_position: 1
description: Chạy được toàn bộ stack (3 service + Gateway + SQL + RabbitMQ) trên máy local trong 10 phút.
tags: [tutorial, getting-started, docker]
---

# Tutorial — Setup project Hdos local

> **Loại:** Tutorial · **Thời lượng:** ~10 phút · **Pre-req:** Docker, .NET 8 SDK

Sau bài này bạn có:

- 3 service Hdos chạy được (Auth, Order, Notification)
- Gateway forward request thành công
- Login lấy được JWT
- Tạo order và thấy notification realtime

## 1. Clone repo

```bash
git clone https://github.com/your-org/hdos.git
cd hdos
```

## 2. Up infrastructure (SQL Server + RabbitMQ)

```bash
docker compose up -d sqlserver rabbitmq
```

Đợi ~30 giây cho SQL Server ready, kiểm tra:

```bash
docker compose ps
# sqlserver  ... healthy
# rabbitmq   ... healthy (UI ở http://localhost:15672, guest/guest)
```

## 3. Apply migrations

```bash
cd src/Services/AuthService/AuthService.API
dotnet ef database update

cd ../../OrderService/OrderService.API
dotnet ef database update

cd ../../NotificationService/NotificationService.API
dotnet ef database update
```

## 4. Run 3 service + Gateway

Mở 4 terminal, mỗi terminal chạy 1:

```bash
# Terminal 1
cd src/Services/AuthService/AuthService.API && dotnet run

# Terminal 2
cd src/Services/OrderService/OrderService.API && dotnet run

# Terminal 3
cd src/Services/NotificationService/NotificationService.API && dotnet run

# Terminal 4
cd src/ApiGateway && dotnet run
```

## 5. Test end-to-end

```bash
# Đăng ký
curl -X POST http://localhost:5000/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"alice@hdos.io","fullName":"Alice","password":"secret123"}'

# Login lấy token
TOKEN=$(curl -s -X POST http://localhost:5000/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"alice@hdos.io","password":"secret123"}' \
  | jq -r '.data.token')

# Tạo order
curl -X POST http://localhost:5000/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"items":[{"productId":"abc","quantity":1,"price":100}]}'
```

Mong đợi: response 201 Created, RabbitMQ UI thấy event `OrderCreatedIntegrationEvent` trong queue `notification.order-created`.

## Kế tiếp

- [How-to: Thêm endpoint REST mới](../how-to/add-rest-endpoint)
- [Explanation: Clean Architecture trong Hdos](../explanation/why-clean-architecture)
- [Sơ đồ C4 Container](../explanation/c4/container)

## Troubleshooting

| Lỗi | Nguyên nhân thường gặp |
|---|---|
| `Cannot connect to SQL Server` | Container chưa healthy, chờ thêm. |
| `401 Unauthorized` ngay khi tạo order | Token chưa attach hoặc hết hạn — xem [Debug 401](../how-to/debug-401). |
| RabbitMQ không nhận event | Sai exchange name, check `appsettings.json` `RabbitMq:Exchange`. |
