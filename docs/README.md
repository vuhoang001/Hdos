# Tài liệu kiến trúc Hdos

Bộ tài liệu này mô tả chi tiết kiến trúc, cách tổ chức code và cách hoạt động
của từng feature trong hệ thống microservices Hdos (.NET 8 + Clean Architecture +
DDD nhẹ + CQRS qua MediatR + EF Core + RabbitMQ + gRPC + YARP).

## Mục lục

| #  | Tài liệu                                          | Nội dung chính                                              |
|----|---------------------------------------------------|-------------------------------------------------------------|
| 01 | [Tổng quan kiến trúc](./01-kien-truc-tong-quan.md) | High-level diagram, các thành phần, dòng dữ liệu sync/async |
| 02 | [Cấu trúc thư mục](./02-cau-truc-thu-muc.md)       | Solution layout, dependency rule giữa các project           |
| 03 | [Building Blocks](./03-building-blocks.md)         | SharedKernel, Contracts, Common (middleware/event bus/log)  |
| 04 | [Feature — AuthService](./04-feature-auth.md)      | Register, Login, GetUser — chi tiết từng bước                |
| 05 | [Feature — OrderService](./05-feature-order.md)    | CreateOrder (có gọi gRPC), GetOrder                          |
| 06 | [Feature — NotificationService](./06-feature-notification.md) | Consume event và List notifications                |
| 07 | [gRPC giữa các service](./07-grpc.md)              | Hợp đồng `users.proto`, server ở Auth, client ở Order        |
| 08 | [Messaging RabbitMQ](./08-rabbitmq.md)             | Topic exchange, publisher, consumer, retry/poison-pill       |
| 09 | [API Gateway (YARP)](./09-api-gateway.md)          | Routing, cluster, request logging                            |
| 10 | [Thêm feature / service mới](./10-them-feature-moi.md) | Checklist từng bước                                       |
| 11 | [Domain Event Dispatcher](./11-domain-events.md)   | Cơ chế bắn domain event in-process qua MediatR + EF Core interceptor |
| 12 | [Testing](./12-testing.md)                          | Test layout, stack (xUnit + FluentAssertions + NSubstitute), cách chạy |
| 13 | [EF Core Migrations](./13-migrations.md)            | Tạo / apply / rollback migration, sinh SQL script, troubleshooting    |
| 14 | [Bảo mật: đóng cổng nội bộ + JWT](./14-bao-mat-jwt.md) | Chỉ Gateway lộ ra ngoài, JWT bắt buộc cho /orders /notifications     |
| 15 | [Realtime SignalR](./15-signalr.md)                | NotificationHub, push noti realtime, auth qua `?access_token=`        |
| 16 | [Luồng request & auth](./16-luong-request-auth.md) | Trace 1 request qua Gateway → service, pipeline order, validate JWT 2 tầng |

## Đọc theo kịch bản

- **Mới vào dự án** → 01 → 02 → 03 → 04 (đọc 1 feature ví dụ).
- **Cần thêm endpoint REST mới** → 04 hoặc 05 (xem mẫu tương tự) → 10.
- **Cần gọi service khác đồng bộ** → 07 (gRPC).
- **Cần phát/nhận event giữa service** → 08 (RabbitMQ).
- **Cần thêm route ở Gateway** → 09.
- **Hỏi tại sao orders/notifications cần token, gateway lộ port nào** → 14.
- **Muốn hiểu request đi từng bước, JWT được check ở đâu, debug 401** → 16.

## Quy ước trong tài liệu

- Đường dẫn file dùng tương đối từ repo root, ví dụ `src/Services/AuthService/AuthService.API/Program.cs:12`.
- Các đoạn code minh họa được trích nguyên văn từ source — nếu code thay đổi
  hãy đồng bộ lại tài liệu.
- Thuật ngữ kỹ thuật (CQRS, AggregateRoot, IntegrationEvent…) giữ nguyên tiếng Anh.
