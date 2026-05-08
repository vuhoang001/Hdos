---
title: Domain Event vs Integration Event
sidebar_position: 2
description: Hai loại event dễ nhầm, khác nhau scope, transport, semantics.
tags: [explanation, events, ddd]
---

# Explanation — Domain Event vs Integration Event

Hai khái niệm này **dễ nhầm** vì cùng tên gọi "event". Phân biệt rõ giúp tránh design lệch.

| | Domain Event | Integration Event |
|---|---|---|
| **Phạm vi** | Trong cùng 1 process (1 service) | Giữa các service |
| **Transport** | MediatR `IPublisher` (in-memory) | RabbitMQ topic exchange |
| **Khi nào fire** | Tự động sau `SaveChanges` thành công | Handler gọi tay `IEventBus.PublishAsync` |
| **Handler thấy lỗi** | Cùng request, status 500 | Consumer retry/drop, publisher không thấy |
| **Kiểu dữ liệu** | Domain object reference được | Phải JSON-serializable phẳng |
| **Coupling** | Cùng deploy, cùng schema | Loose, evolvable |

## Quy tắc dùng

- **Domain event** = "việc nội bộ vừa xảy ra". VD: log audit, raise sub-task trong cùng service.
- **Integration event** = "thông báo cho thế giới ngoài service". Khi service khác cần phản ứng.

## Pattern textbook: kết hợp cả hai

```
[Use case]
   user.RecordLogin()                   ← raise UserLoggedInDomainEvent
   _uow.SaveChangesAsync(ct)            ← interceptor dispatch domain event
                │
                ▼
   [UserLoggedInDomainEventHandler]    ← in-process
        │
        ├── log audit
        └── _eventBus.PublishAsync(new UserLoggedInIntegrationEvent(...))
                │
                ▼
                RabbitMQ → NotificationService consume
```

## Vì sao tách?

- Domain event là **một phần của business model**: aggregate biết khi nó thay đổi.
- Integration event là **detail về cách share thông tin**: có thể đổi từ RabbitMQ → Kafka mà domain không biết.

Trộn lẫn = leak transport detail vào Domain layer = vi phạm dependency rule (xem [Why Clean Architecture](./why-clean-architecture)).
