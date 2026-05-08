---
title: C4 Level 1 — System Context
sidebar_position: 1
description: Hdos đặt trong context — actor và external system.
tags: [explanation, c4, architecture, diagram]
---

# C4 Level 1 — System Context

> **C4 Model**: 4 levels — Context (zoom out nhất) → Container → Component → Code.

Mục đích level 1: **non-technical reader** cũng đọc được. Trả lời:
*Hdos là gì, ai dùng, nó nói chuyện với hệ ngoài nào?*

## Diagram

```mermaid
flowchart TB
    classDef person fill:#08427b,stroke:#052e56,color:#fff
    classDef system fill:#1168bd,stroke:#0b4884,color:#fff
    classDef external fill:#999999,stroke:#6b6b6b,color:#fff

    User["👤 End User<br/><i>Customer của Hdos</i>"]:::person
    Admin["👤 Admin<br/><i>Quản trị</i>"]:::person

    Hdos["📦 Hdos Platform<br/><i>Microservices .NET 8</i><br/>Quản lý user, order, notification"]:::system

    Email["📧 Email Provider<br/><i>SMTP/SendGrid</i>"]:::external
    Vault["🔐 Secret Store<br/><i>Vault / Azure KV</i>"]:::external

    User -->|"Đăng ký, đặt hàng<br/>HTTPS"| Hdos
    Admin -->|"Quản lý user/order<br/>HTTPS"| Hdos
    Hdos -->|"Gửi email noti<br/>SMTP"| Email
    Hdos -->|"Đọc JWT secret<br/>HTTPS"| Vault
```

## Chú thích

| Phần tử | Vai trò |
|---|---|
| 👤 **End User** | Người dùng cuối — gọi REST API qua FE/Postman |
| 👤 **Admin** | Quản trị — view dashboard, list orders |
| 📦 **Hdos Platform** | Hệ thống bạn đang xây — *zoom in ở [Container](./container)* |
| 📧 **Email Provider** | Bên ngoài — gửi email khi `UserRegistered` |
| 🔐 **Secret Store** | Bên ngoài — lưu JWT secret, DB password |

## Kế tiếp

Zoom vào trong Hdos: [C4 Container](./container).
