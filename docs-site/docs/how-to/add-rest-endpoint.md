---
title: How to thêm 1 REST endpoint
sidebar_position: 2
description: Checklist từng bước thêm 1 endpoint mới qua đủ 4 layer.
tags: [how-to, cqrs, mediatr]
---

# How-to — Thêm 1 REST endpoint mới

Checklist nhanh khi muốn thêm endpoint vào service có sẵn (ví dụ `OrderService`).

## Checklist

- [ ] **Domain** — thêm method vào aggregate hoặc tạo entity mới
- [ ] **Application** — folder `Features/<UseCase>/` chứa Command/Query + Validator + Handler
- [ ] **Infrastructure** — repository implementation nếu có query mới
- [ ] **API** — action trong controller, gọi `_sender.Send(cmd)`
- [ ] **Migration** nếu có thay đổi schema → xem [How-to: Create migration](./create-migration)
- [ ] **Test** — unit test cho Handler, integration test nếu chạm DB
- [ ] **Docs** — cập nhật reference nếu là endpoint public

## Mẫu file

```
Features/CancelOrder/
├── CancelOrderCommand.cs        # record + Validator + Handler trong 1 file
├── CancelOrderRequest.cs        # DTO request HTTP
└── CancelOrderResponse.cs       # DTO response (nếu có)
```

## Convention

- 1 file = 1 use case. **Không** tách Commands/ Queries/ Handlers ra folder riêng.
- Validator dùng FluentValidation, đặt cùng file Command.
- Response wrap qua `ApiResponse<T>` — controller không tự build JSON.

Chi tiết: [Explanation: Why Clean Architecture](../explanation/why-clean-architecture).
