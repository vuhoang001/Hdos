---
title: How to tạo EF Core migration
sidebar_position: 3
description: Tạo / apply / rollback migration cho 1 service.
tags: [how-to, ef-core, database]
---

# How-to — Tạo EF Core migration

## Tạo migration mới

```bash
cd src/Services/AuthService/AuthService.API

dotnet ef migrations add AddUserPhoneNumber \
  --project ../AuthService.Infrastructure \
  --startup-project .
```

## Apply lên DB

```bash
dotnet ef database update \
  --project ../AuthService.Infrastructure \
  --startup-project .
```

## Rollback về migration trước

```bash
dotnet ef database update <PreviousMigrationName> \
  --project ../AuthService.Infrastructure \
  --startup-project .
```

## Sinh SQL script (cho prod review)

```bash
dotnet ef migrations script \
  --project ../AuthService.Infrastructure \
  --startup-project . \
  -o migration.sql
```

## Troubleshooting

| Lỗi | Cách xử |
|---|---|
| `No project was found` | Sai đường dẫn `--project`. Phải trỏ tới Infrastructure (chứa DbContext). |
| `Unable to create DbContext` | Thiếu connection string. Set env `ConnectionStrings__AuthDb=...` hoặc copy từ `appsettings.Development.json`. |
| Migration đã apply nhưng đổi code → muốn regen | `dotnet ef migrations remove` rồi tạo lại. **Chỉ dùng khi chưa push lên main.** |
