---
title: Vì sao chọn Clean Architecture?
sidebar_position: 1
description: Triết lý đứng sau cách tổ chức Domain / Application / Infrastructure / API.
tags: [explanation, architecture, ddd]
---

# Explanation — Vì sao chọn Clean Architecture?

> **Loại:** Explanation · **Mục đích:** Hiểu *why*, không phải *how*.

## Vấn đề muốn giải

Trước đây dự án dạng **CRUD truyền thống**:

- Controller chứa logic
- Service gọi DbContext trực tiếp
- Đổi 1 field → phải sửa 5 file rải rác
- Test phải spin up DB → unit test biến thành integration test

## Quy tắc lõi

```
API ──► Application ──► Domain
 │           ▲
 └──► Infrastructure ──► Application
```

Đọc: **dependency chỉ chảy vào trong**. Domain không biết EF Core, không biết RabbitMQ.

## Hệ quả thực tế

| Thay đổi | Phải sửa |
|---|---|
| Đổi SQL Server → Postgres | Chỉ Infrastructure + connection string |
| Đổi gRPC → HTTP cho user lookup | Chỉ impl `IUserLookupService` trong `OrderService.Infrastructure` |
| Đổi RabbitMQ → Kafka | Chỉ `BuildingBlocks/Common/Messaging` |
| Thêm endpoint mới | 1 folder `Features/<UseCase>/` + 1 action controller |

## Đánh đổi

✅ **Được**:
- Test `Application` thuần — không cần DB
- Đổi infra không động vào business logic
- Stack trace đọc rõ tầng nào lỗi

❌ **Mất**:
- Boilerplate hơn CRUD truyền thống — 1 use case = 4 file (Domain method, Command, Handler, Controller action)
- Curve học cho dev mới — phải hiểu DI + MediatR + Result pattern

## Khi nào KHÔNG nên dùng

- Service nhỏ &lt;5 endpoint, không có business rule phức tạp → CRUD thẳng nhanh hơn
- Dự án sống &lt;6 tháng → ROI âm

Xem thêm:

- [ADR-0001: Record architecture decisions](../adr/0001-record-architecture-decisions)
- [C4 Container diagram](./c4/container)
