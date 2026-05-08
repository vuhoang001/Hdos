---
title: How to thêm service mới (đa ngôn ngữ)
sidebar_position: 6
description: Checklist thêm service mới vào monorepo — Node.js, Python, Go hay .NET đều dùng cùng pipeline.
tags: [how-to, microservices, docker, cicd]
---

# How-to — Thêm service mới (bất kỳ ngôn ngữ)

> **CI/CD không quan tâm ngôn ngữ.** Pipeline chỉ nhìn vào `Dockerfile`.
> Bạn chỉ cần sửa 3 file config, phần còn lại tự động.

---

## Checklist

- [ ] Tạo thư mục service trong `src/Services/<TênService>/`
- [ ] Viết `Dockerfile` trong thư mục đó
- [ ] Thêm entry vào `services.json`
- [ ] Thêm path filter vào `.github/path-filters.yml`
- [ ] Thêm service vào `docker-compose.yml` (local dev)
- [ ] Thêm service vào `docker-compose.prod.yml` (production)
- [ ] Thêm route vào ApiGateway `appsettings.json`
- [ ] Gán owner trong `.github/CODEOWNERS`

---

## Bước 1 — Cấu trúc thư mục

Mỗi ngôn ngữ theo layout riêng nhưng **luôn có `Dockerfile` ở root**:

import Tabs from '@theme/Tabs';
import TabItem from '@theme/TabItem';

<Tabs>
<TabItem value="dotnet" label=".NET (hiện tại)">

```
src/Services/PaymentService/
├── PaymentService.API/
│   ├── Dockerfile
│   ├── Program.cs
│   └── PaymentService.API.csproj
├── PaymentService.Application/
├── PaymentService.Domain/
└── PaymentService.Infrastructure/
```

Build context nên là repo root (`.`) để dùng được `BuildingBlocks`.

</TabItem>
<TabItem value="nodejs" label="Node.js / TypeScript">

```
src/Services/PaymentService/
├── Dockerfile
├── package.json
├── tsconfig.json
├── src/
│   ├── index.ts
│   ├── routes/
│   └── services/
└── tests/
```

```dockerfile
FROM node:22-alpine AS builder
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production
COPY --from=builder /app/dist ./dist
COPY --from=builder /app/node_modules ./node_modules
EXPOSE 8080
CMD ["node", "dist/index.js"]
```

</TabItem>
<TabItem value="python" label="Python / FastAPI">

```
src/Services/ReportService/
├── Dockerfile
├── pyproject.toml
├── src/
│   ├── main.py
│   └── api/
└── tests/
```

```dockerfile
FROM python:3.12-slim AS builder
WORKDIR /app
COPY pyproject.toml ./
RUN pip install --no-cache-dir .

FROM python:3.12-slim AS runtime
WORKDIR /app
ENV PYTHONUNBUFFERED=1
COPY src/ ./src/
EXPOSE 8080
CMD ["uvicorn", "src.main:app", "--host", "0.0.0.0", "--port", "8080"]
```

</TabItem>
<TabItem value="go" label="Go">

```
src/Services/AnalyticsService/
├── Dockerfile
├── go.mod
├── cmd/server/main.go
└── internal/
    ├── api/
    └── domain/
```

```dockerfile
FROM golang:1.23-alpine AS builder
WORKDIR /app
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -o server ./cmd/server

FROM alpine:3.20 AS runtime
WORKDIR /app
COPY --from=builder /app/server .
EXPOSE 8080
CMD ["./server"]
```

</TabItem>
</Tabs>

---

## Bước 2 — Thêm vào `services.json`

```json
{
  "paymentservice": {
    "dockerfile": "src/Services/PaymentService/Dockerfile",
    "context": "src/Services/PaymentService"
  }
}
```

> Dùng `"context": "."` (repo root) nếu service .NET cần tham chiếu `BuildingBlocks`.
> Node.js / Python / Go thường dùng context là thư mục của service.

---

## Bước 3 — Thêm vào `.github/path-filters.yml`

```yaml
paymentservice:
  - "src/Services/PaymentService/**"
```

Chỉ khi service dùng `BuildingBlocks` thì thêm:

```yaml
  - "src/BuildingBlocks/**"
```

---

## Bước 4 — Thêm vào docker-compose

**`docker-compose.yml`** (local dev):

```yaml
paymentservice:
  image: hdos/paymentservice:latest
  build:
    context: src/Services/PaymentService
    dockerfile: Dockerfile
  environment:
    PORT: "8080"
  networks: [hdos-net]
```

**`docker-compose.prod.yml`** (production):

```yaml
paymentservice:
  image: ghcr.io/${GHCR_OWNER}/hdos-paymentservice:latest
  build: !reset null
  environment:
    NODE_ENV: production
```

---

## Bước 5 — Thêm route vào ApiGateway

Trong `src/ApiGateway/appsettings.json`:

```json
{
  "RouteId": "payment-route",
  "ClusterId": "payment-cluster",
  "Match": { "Path": "/payments/{**catch-all}" },
  "Transforms": [{ "PathRemovePrefix": "/payments" }]
}
```

Và thêm cluster:

```json
{
  "ClusterId": "payment-cluster",
  "Destinations": {
    "payment/d1": { "Address": "http://paymentservice:8080/" }
  }
}
```

---

## Bước 6 — Gán owner trong CODEOWNERS

Trong `.github/CODEOWNERS`:

```
src/Services/PaymentService/  @github-username-developer
```

---

## Xác nhận pipeline chạy đúng

Sau khi push nhánh mới và tạo PR:

1. Vào **GitHub → Actions → CI** — kiểm tra job `Build & Push (paymentservice)` xuất hiện
2. Merge vào `main` → **CD** tự deploy
3. Trên server: `docker compose ps` kiểm tra container `paymentservice` đang chạy
