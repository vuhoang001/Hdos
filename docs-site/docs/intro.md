---
slug: /
title: Hdos Documentation
sidebar_position: 1
---

# Hdos Docs

Tài liệu kiến trúc cho hệ microservices **Hdos** — .NET 8, Clean Architecture, CQRS qua MediatR, EF Core, RabbitMQ, gRPC, YARP.

Site được tổ chức theo combo **Diátaxis + C4 + ADR**:

| Loại tài liệu | Khi nào đọc | Ví dụ |
|---|---|---|
| 🚀 **Tutorials** | Mới vào dự án, học từ 0 | [Setup project local](./tutorials/setup-project) |
| 🛠️ **How-to** | Đang stuck với 1 task cụ thể | [Add authentication](./how-to/add-authentication) |
| 📖 **Reference** | Cần tra cứu nhanh | [API overview](./reference/api-overview) |
| 💡 **Explanation** | Muốn hiểu vì sao thiết kế thế này | [Why Clean Architecture](./explanation/why-clean-architecture) |
| 📐 **ADR** | Tra quyết định kiến trúc đã chốt | [ADR Index](./adr/) |

## Đọc theo kịch bản

- **Mới vào dự án** → Tutorial *Setup project* → Explanation *Why Clean Architecture* → C4 *Context*.
- **Cần thêm endpoint REST** → How-to *Add REST endpoint*.
- **Debug 401** → How-to *Debug 401*.
- **Hiểu cấu trúc tổng** → Explanation *C4 Container*.

## Quy ước

- File code có prefix `src/...` là **đường dẫn tương đối từ repo root**.
- Code excerpt ở dạng ` ```csharp:src/path/file.cs ` để Docusaurus highlight + reader copy được.
- Thuật ngữ kỹ thuật (CQRS, AggregateRoot, IntegrationEvent…) giữ nguyên tiếng Anh — xem [Glossary](./reference/glossary).
