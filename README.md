# Hdos

Nền tảng quản lý bệnh viện xây dựng trên kiến trúc **microservices** với .NET 8, Clean Architecture, và DDD. Mục tiêu là demo đầy đủ một hệ thống production-ready: từ authentication, giao tiếp nội bộ, real-time notifications, đến observability và CI/CD.

---

## Kiến trúc

```
Browser / Mobile
      │  HTTP
      ▼
 nginx :5000          ← API Gateway duy nhất, xử lý CORS + JWT validation
  ├── /auth/*         → AuthService :8080 (+ gRPC :8081)
  ├── /orders/*       → OrderService :8080          ┐ Sync (REST)
  ├── /notifications/*→ NotificationService :8080   ┘
  ├── /m01/*          → M01Service :8080
  └── /async/*        → AsyncGateway :8080           ← Async (HTTP → Queue)
                             │
                        RabbitMQ hdos.events
                             ├── OrderCreateRequested      → OrderService
                             └── NotificationSendRequested → NotificationService

Giao tiếp nội bộ (sync):
  OrderService ──gRPC──► AuthService          (xác nhận user tồn tại)
  AuthService  ──────┐
  OrderService ───── ├──► RabbitMQ hdos.events ──► NotificationService
```

---

## Tech Stack

| Layer | Công nghệ |
|-------|-----------|
| API Gateway | nginx (TLS, CORS, reverse proxy — không verify JWT) |
| Services | .NET 8 / ASP.NET Core — Clean Architecture + DDD |
| CQRS | MediatR + FluentValidation |
| Database | SQL Server — EF Core Code First (1 DB / service) |
| Sync comm | gRPC (Protobuf, HTTP/2) |
| Async comm | RabbitMQ — topic exchange |
| Real-time | Server-Sent Events (SSE) |
| Auth | JWT HS256 (AuthService issue token chứa `roles` + `permission` claims; mỗi service tự verify + enforce policy) |
| Metrics | Prometheus + prometheus-net |
| Logs | Serilog → Grafana Loki |
| Tracing | OpenTelemetry → Grafana Tempo (W3C Trace Context) |
| Dashboards | Grafana |
| CI | GitHub Actions — detect-changes, matrix build, push lên GHCR |
| CD | GitHub Actions self-hosted runner — auto staging, manual production |

---

## Khởi động nhanh

```bash
git clone https://github.com/vuhoang001/Hdos.git
cd Hdos

# Chạy toàn bộ stack
docker compose up -d

# Kiểm tra
curl http://localhost:5000/health
```

Swagger từng service:

| URL | Service |
|-----|---------|
| http://localhost:5000/auth/swagger | AuthService |
| http://localhost:5000/orders/swagger | OrderService |
| http://localhost:5000/notifications/swagger | NotificationService |
| http://localhost:5000/m01/swagger | M01Service |
| http://localhost:5000/async/swagger | **AsyncGateway** (Async API) |
| http://localhost:5000/dm/swagger | **DataMatchingService** |
| https://localhost:8443/superset/ | **Apache Superset** (BI — admin/admin, xem [docs/64](./docs/64-superset-phase1-standalone.md)) |
| http://localhost:15672 | RabbitMQ Management (guest/guest) |

> **Test API qua Swagger**: gọi `POST /auth/login` (`admin@hdos.dev` / `Admin1234!`) → copy `data.token` → bấm **Authorize** trong Swagger → paste token. Chi tiết: [docs/06-xac-thuc.md](./docs/06-xac-thuc.md).

### Bật monitoring (Prometheus + Loki + Tempo + Grafana)

```bash
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d
```

| URL | Công cụ |
|-----|---------|
| http://localhost:3030 | Grafana (admin/admin) |
| http://localhost:9090 | Prometheus |

---

## Cấu trúc dự án

```
Hdos/
├── src/
│   ├── ApiGateway/            ← Async API Gateway: HTTP → RabbitMQ (no DB, Swagger docs)
│   ├── BuildingBlocks/        ← Shared: SharedKernel, Common, Contracts
│   └── Services/
│       ├── AuthService/       ← User, JWT, gRPC server
│       ├── OrderService/      ← Order, gRPC client + queue consumer
│       ├── NotificationService/ ← RabbitMQ consumers, SSE
│       ├── M01Service/        ← Nghiệp vụ bệnh viện
│       └── DataMatchingService/ ← Ingest + dedup + matching + báo cáo
├── tests/                     ← xUnit + FluentAssertions + NSubstitute
├── nginx/nginx.conf           ← API Gateway config
├── monitoring/                ← Prometheus, Loki, Tempo, Grafana config
├── docker-compose.yml
├── docker-compose.server.yml  ← Override cho staging/production
├── docker-compose.monitoring.yml
└── services.json              ← Map service → Dockerfile (dùng bởi CI)
```

Mỗi service theo 4 layer: `Domain → Application → Infrastructure → API`.

---

## Tài liệu

Xem thư mục [`docs/`](./docs/README.md) — 13 file giải thích chi tiết từng phần của hệ thống, bao gồm lý do tại sao các quyết định kỹ thuật được đưa ra.

| File | Nội dung |
|------|----------|
| [01 — Tổng quan kiến trúc](./docs/01-tong-quan-kien-truc.md) | Sơ đồ hệ thống, lý do chọn từng công nghệ |
| [05 — Nginx Gateway](./docs/05-nginx-gateway.md) | Config đầy đủ, CORS, JWT validation |
| [07 — Giao tiếp nội bộ](./docs/07-giao-tiep-noi-bo.md) | gRPC + RabbitMQ |
| [08 — Quan sát hệ thống](./docs/08-quan-sat-he-thong.md) | Prometheus, Loki, Tempo, Grafana |
| [10 — CI/CD Pipeline](./docs/10-cicd-pipeline.md) | GitHub Actions |
| [13 — Thêm tính năng](./docs/13-them-tinh-nang.md) | Checklist thêm endpoint / event / service |
| [15 — Async Gateway](./docs/15-async-gateway.md) | HTTP → Queue pattern, AsyncGateway service |
| [16 — Test Async Gateway](./docs/16-test-async-gateway.md) | Luồng test đầy đủ, xem RabbitMQ Management UI |
| [23 — DataMatchingService](./docs/23-data-matching-service.md) | Ingest, dedup SHA-256, MatchingWorker, báo cáo y tế |
| [24 — Dashboard Engine](./docs/24-dashboard-data-matching.md) | Dashboard config pattern, ingest HIS → sections[], test M02 |
| [25 — Server-Driven UI](./docs/25-sdui-server-driven-ui.md) | SDUI engine, component types, test executive page, thêm page mới |
