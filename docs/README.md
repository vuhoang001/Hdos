# Hdos — Tài liệu kỹ thuật

Bộ tài liệu này mô tả toàn bộ hệ thống **Hdos** — một nền tảng quản lý bệnh viện xây dựng trên kiến trúc microservices với .NET 8. Mục tiêu: người mới vào team sau 6 tháng vẫn đọc được, hiểu được, và tự làm được.

---

## Mục lục

| File | Nội dung |
|------|----------|
| [01 — Tổng quan kiến trúc](./01-tong-quan-kien-truc.md) | Sơ đồ hệ thống, lý do chọn từng công nghệ |
| [02 — Cấu trúc dự án](./02-cau-truc-du-an.md) | Folder layout, conventions đặt tên |
| [03 — Building Blocks](./03-building-blocks.md) | Thư viện dùng chung: SharedKernel, Common, Contracts |
| [04 — Các Services](./04-cac-services.md) | AuthService, OrderService, NotificationService, M01Service |
| [05 — Nginx Gateway](./05-nginx-gateway.md) | Reverse proxy: TLS, CORS, routing theo prefix (nginx không verify JWT) |
| [06 — Xác thực & Phân quyền](./06-xac-thuc.md) | Custom JWT HS256 với permission claims, Register/Login, RBAC, seed admin/testuser |
| [07 — Giao tiếp nội bộ](./07-giao-tiep-noi-bo.md) | gRPC (sync) + RabbitMQ (async) |
| [08 — Quan sát hệ thống](./08-quan-sat-he-thong.md) | Prometheus, Loki, Tempo, Grafana, W3C distributed tracing |
| [10 — CI/CD Pipeline](./10-cicd-pipeline.md) | GitHub Actions build → test → push → deploy |
| [11 — Local Dev & Deploy](./11-local-dev-va-deploy.md) | Setup máy local, chạy monitoring, deploy production |
| [12 — Kiểm thử](./12-kiem-thu.md) | Strategy, stack, cách chạy tests |
| [13 — Thêm tính năng](./13-them-tinh-nang.md) | Checklist thêm endpoint, event, service mới |
| [14 — SignalR Realtime](./14-signalr-realtime.md) | Hub, envelope chuẩn, cách test từ frontend |
| [15 — Async Gateway](./15-async-gateway.md) | HTTP→Queue→Service, endpoints, test guide, Grafana observability |
| [16 — HTTPS & TLS](./16-https-ssl.md) | Self-signed cert cho nginx, HTTPS termination tại 8443, hướng dẫn production thay cert thật |
| [17 — MassTransit Messaging](./17-masstransit-messaging.md) | MassTransit + RabbitMQ: publish/consume pattern, queue naming, retry, dead-letter, test endpoint |
| [19 — Test End-to-End Publish→Consumer](./19-test-masstransit-e2e.md) | Demo chạy thực tế: lấy token, publish event, verify consumer log & RabbitMQ UI, troubleshooting |
| [20 — Adapter Ingest Gateway](./20-adapter-ingest-gateway.md) | External Project bắn data vào → song song ghi Lakehouse + push realtime Frontend |

---

## Kiến trúc một dòng

```
Browser / Mobile
       │  HTTPS
       ▼
  nginx :8443 (SSL)          ← API Gateway duy nhất ra ngoài
  ├── /auth/*    → AuthService (login, register, validate)
  ├── /orders/*  → OrderService
  ├── /notifications/* → NotificationService
  ├── /m01/*     → M01Service
  ├── /async/*   → AsyncGateway
  └── /          → Frontend :4000
       │
       ├── SQL Server  (mỗi service 1 database)
       ├── RabbitMQ    (async events giữa services)
       └── gRPC        (sync calls giữa services)
```

---

## Khởi động nhanh (local)

```bash
# 1. Clone
git clone https://github.com/vuhoang001/Hdos.git && cd Hdos

# 2. Chạy toàn bộ stack
docker compose up -d

# 3. Kiểm tra health
curl http://localhost:5000/health

# 4. (Tuỳ chọn) Bật monitoring
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d
```

Sau khi chạy:

| URL | Mô tả |
|-----|-------|
| `https://localhost:8443` | API Gateway + Frontend (HTTPS) |
| `http://localhost:5000` | HTTP → redirect 301 sang HTTPS |
| `https://localhost:8443/auth/swagger` | Swagger AuthService |
| `https://localhost:8443/orders/swagger` | Swagger OrderService |
| `https://localhost:8443/notifications/swagger` | Swagger NotificationService |
| `https://localhost:8443/m01/swagger` | Swagger M01Service |
| `http://localhost:15672` | RabbitMQ Management (guest/guest) |
| `http://localhost:3030` | Grafana (admin/admin) |
| `http://localhost:9090` | Prometheus |

> **Lưu ý cert:** Lần đầu vào `https://localhost:8443`, browser cảnh báo self-signed cert → click **Advanced → Proceed** một lần là xong.
