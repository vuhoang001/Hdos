---
title: Cấu trúc thư mục
sidebar_position: 2
description: Solution layout, dependency rule giữa các project.
tags: [reference, structure]
---

# Cấu trúc thư mục

```
Hdos/
├── Hdos.sln
├── docker-compose.yml
├── docs/                                 # Markdown gốc (legacy, sẽ migrate sang docs-site)
├── docs-site/                            # Docusaurus site này
└── src/
    ├── ApiGateway/                       # YARP reverse proxy
    ├── BuildingBlocks/
    │   ├── SharedKernel/                 # DDD primitives
    │   ├── Contracts/                    # IntegrationEvents + Protos
    │   └── Common/                       # Behaviors, Messaging, Middleware…
    └── Services/
        ├── AuthService/
        │   ├── AuthService.Domain/
        │   ├── AuthService.Application/
        │   ├── AuthService.Infrastructure/
        │   └── AuthService.API/
        ├── OrderService/
        └── NotificationService/
```

## Dependency rule

Mũi tên `A → B` đọc là "A reference B":

```
API ──► Application ──► Domain ──► SharedKernel
 │           ▲
 └──► Infrastructure ──► Application
```

**Không bao giờ ngược chiều**. Domain không biết Infrastructure tồn tại.

## Naming convention

| Project | Namespace | Assembly |
|---|---|---|
| `SharedKernel` | `Hdos.SharedKernel` | `Hdos.SharedKernel` |
| `Contracts` | `Hdos.Contracts.*` | `Hdos.Contracts` |
| `Common` | `Hdos.Common.*` | `Hdos.Common` |
| `<Service>.Domain` | `Hdos.<Service>.Domain.*` | `Hdos.<Service>.Domain` |
| `<Service>.Application` | `Hdos.<Service>.Application.*` | `Hdos.<Service>.Application` |
| `<Service>.Infrastructure` | `Hdos.<Service>.Infrastructure.*` | `Hdos.<Service>.Infrastructure` |
| `<Service>.API` | `Hdos.<Service>.API.*` | `Hdos.<Service>.API` |
