---
title: Glossary
sidebar_position: 99
description: Định nghĩa thuật ngữ DDD/CQRS/MediatR dùng trong dự án.
tags: [reference, glossary]
---

# Glossary

| Thuật ngữ | Định nghĩa |
|---|---|
| **Aggregate** | Cụm entity được commit như một đơn vị atomic. Có 1 root duy nhất. |
| **AggregateRoot** | Entity entry-point của aggregate. Kế thừa `BaseEntity` + `IHasDomainEvents`. |
| **Value Object** | Object định danh bằng *giá trị*, immutable. VD: `Email`, `Money`. |
| **Domain Event** | Sự kiện in-process, dispatch sau `SaveChanges` qua MediatR. |
| **Integration Event** | Sự kiện cross-service, đi qua RabbitMQ topic exchange. |
| **CQRS** | Tách Command (mutate) và Query (read). Trong Hdos dùng MediatR. |
| **MediatR Pipeline** | Chuỗi `IPipelineBehavior` chạy trước handler: Logging → Validation → Handler. |
| **Result `<T>`** | Wrapper trả về thay vì throw cho expected failures (sai password, not found). |
| **Repository** | Abstraction CRUD aggregate, interface ở Domain, impl ở Infrastructure. |
| **UnitOfWork** | Wrapper `SaveChangesAsync` để Application không phụ thuộc DbContext. |
| **YARP** | Yet Another Reverse Proxy — Microsoft's reverse proxy library. |
| **JWT HS256** | JSON Web Token ký bằng HMAC-SHA256 (symmetric). Hdos đang dùng. |
| **JWT RS256** | JWT ký bằng RSA (asymmetric). Hướng nâng cấp tương lai (xem ADR). |
