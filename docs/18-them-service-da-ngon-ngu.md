# 18 — Thêm service mới & làm việc nhiều người

## 1. Quy tắc cốt lõi

> **CI/CD không quan tâm ngôn ngữ.** Pipeline chỉ nhìn vào `Dockerfile`.
> Node.js, Python, Go, Rust... đều dùng chung một workflow, chỉ Dockerfile khác.

Mỗi developer làm việc **độc lập trong thư mục service của mình**. Path filter
đảm bảo chỉ service đó được rebuild, không ảnh hưởng service khác.

---

## 2. Cấu trúc thư mục cho service bất kỳ ngôn ngữ

### .NET (hiện tại — AuthService, OrderService...)
```
src/Services/AuthService/
├── AuthService.API/
│   ├── Dockerfile          ← Build context là repo root
│   ├── Program.cs
│   └── ...
├── AuthService.Application/
├── AuthService.Domain/
└── AuthService.Infrastructure/
```

### Node.js / TypeScript
```
src/Services/PaymentService/
├── Dockerfile
├── package.json
├── tsconfig.json
├── src/
│   ├── index.ts
│   ├── routes/
│   ├── services/
│   └── models/
└── tests/
```

### Python / FastAPI
```
src/Services/ReportService/
├── Dockerfile
├── pyproject.toml          ← hoặc requirements.txt
├── src/
│   ├── main.py
│   ├── api/
│   ├── domain/
│   └── infrastructure/
└── tests/
```

### Go
```
src/Services/AnalyticsService/
├── Dockerfile
├── go.mod
├── go.sum
├── cmd/
│   └── server/main.go
├── internal/
│   ├── api/
│   ├── domain/
│   └── infrastructure/
└── tests/
```

**Quy tắc chung cho mọi ngôn ngữ:**
- `Dockerfile` luôn nằm ở root của thư mục service
- `tests/` hoặc `__tests__/` nằm trong service (không phải thư mục `tests/` ngoài cùng — thư mục đó dành cho .NET integration test)
- Service phải có `README.md` mô tả cách chạy local

---

## 3. Checklist thêm service mới (bất kỳ ngôn ngữ)

### Bước 1 — Tạo thư mục và Dockerfile

```bash
mkdir -p src/Services/PaymentService
```

Dockerfile mẫu cho từng ngôn ngữ — xem mục 5 bên dưới.

### Bước 2 — Thêm vào `services.json`

```json
{
  "paymentservice": {
    "dockerfile": "src/Services/PaymentService/Dockerfile",
    "context": "."
  }
}
```

> `context: "."` = build context là repo root. Cần thiết nếu service .NET
> dùng `BuildingBlocks`. Với Node.js/Python thường dùng context là thư mục
> service: `"context": "src/Services/PaymentService"`.

### Bước 3 — Thêm path filter vào `.github/path-filters.yml`

```yaml
paymentservice:
  - "src/Services/PaymentService/**"
  # Nếu service này dùng BuildingBlocks/Contracts thêm:
  # - "src/BuildingBlocks/**"
```

### Bước 4 — Thêm vào `docker-compose.yml` (local dev)

```yaml
paymentservice:
  image: hdos/paymentservice:latest
  build:
    context: src/Services/PaymentService   # hoặc "." nếu cần BuildingBlocks
    dockerfile: Dockerfile
  environment:
    NODE_ENV: development
    PORT: 8080
  networks: [hdos-net]
```

### Bước 5 — Thêm vào `docker-compose.prod.yml` (production)

```yaml
paymentservice:
  image: ghcr.io/${GHCR_OWNER}/hdos-paymentservice:latest
  build: !reset null
  environment:
    NODE_ENV: production
```

### Bước 6 — Thêm route vào ApiGateway (`appsettings.json`)

```json
{
  "RouteId": "payment-route",
  "ClusterId": "payment-cluster",
  "Match": { "Path": "/payments/{**catch-all}" },
  "Transforms": [{ "PathRemovePrefix": "/payments" }]
}
```

### Bước 7 — Thêm vào CODEOWNERS (xem mục 4)

```
src/Services/PaymentService/  @ten-developer-phu-trach
```

---

## 4. Quản lý code ownership — CODEOWNERS

File `.github/CODEOWNERS` tự động gán reviewer khi có PR chạm vào thư mục đó.
Developer A không thể merge code vào service của Developer B mà không có review.

```
# ApiGateway và BuildingBlocks — lead review
src/ApiGateway/              @lead-dev
src/BuildingBlocks/          @lead-dev

# Mỗi service — developer phụ trách
src/Services/AuthService/    @developer-a
src/Services/OrderService/   @developer-b
src/Services/PaymentService/ @developer-c

# CI/CD — chỉ lead được sửa
.github/                     @lead-dev
services.json                @lead-dev
docker-compose*.yml          @lead-dev
```

**Kích hoạt:** Vào GitHub repo → Settings → Branches → Add rule cho `main`
→ bật **Require review from Code Owners**.

---

## 5. Dockerfile mẫu theo ngôn ngữ

### Node.js / TypeScript
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

### Python / FastAPI
```dockerfile
FROM python:3.12-slim AS builder
WORKDIR /app
COPY pyproject.toml ./
RUN pip install --no-cache-dir build && pip install .

FROM python:3.12-slim AS runtime
WORKDIR /app
ENV PYTHONUNBUFFERED=1
COPY --from=builder /usr/local/lib/python3.12 /usr/local/lib/python3.12
COPY src/ ./src/
EXPOSE 8080
CMD ["uvicorn", "src.main:app", "--host", "0.0.0.0", "--port", "8080"]
```

### Go
```dockerfile
FROM golang:1.23-alpine AS builder
WORKDIR /app
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -o server ./cmd/server

FROM alpine:3.20 AS runtime
RUN apk add --no-cache ca-certificates
WORKDIR /app
COPY --from=builder /app/server .
EXPOSE 8080
CMD ["./server"]
```

---

## 6. Git branch strategy cho team nhiều người

```
main          ← production, protected
  │
  ├── feature/auth-login-google        ← Developer A
  ├── feature/order-cancel-endpoint    ← Developer B
  ├── feature/payment-service          ← Developer C (service mới)
  └── fix/notification-reconnect       ← Developer A fix bug
```

**Quy tắc:**

| Tình huống | Branch name |
|---|---|
| Feature mới trong service | `feature/<service>-<tên-feature>` |
| Service mới | `feature/<service-name>-service` |
| Bug fix | `fix/<service>-<mô-tả>` |
| Hotfix production | `hotfix/<mô-tả>` |

Developer chỉ cần tạo PR vào `main`. CODEOWNERS tự động ping đúng người review.
Sau khi merge, CD tự động deploy chỉ service thay đổi.

---

## 7. Giao tiếp giữa services khi team làm song song

Khi Developer A viết service cần gọi sang service của Developer B, dùng:

**Integration Events (async — qua RabbitMQ):**
```
src/BuildingBlocks/Contracts/IntegrationEvents/
    PaymentCompletedIntegrationEvent.cs    ← Developer C tạo event này
```
Developer A subscribe event này trong service của mình mà không cần đợi
Developer C hoàn thiện service — chỉ cần contract event đã có.

**gRPC (sync — cần server đang chạy):**
```
src/BuildingBlocks/Contracts/Protos/
    payment.proto    ← Developer C định nghĩa proto
```
Developer A generate stub từ proto và mock trong unit test.

**Nguyên tắc:** Contracts nằm trong `BuildingBlocks/Contracts`, không nằm
trong service. Đây là vùng chỉ lead hoặc cả 2 developer thống nhất mới được sửa.
