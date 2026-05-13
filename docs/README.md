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
| [05 — Nginx Gateway](./05-nginx-gateway.md) | Config chi tiết, CORS, JWT validation, routing |
| [06 — Xác thực & Phân quyền](./06-xac-thuc-phan-quyen.md) | JWT flow đầu cuối, middleware stack |
| [07 — Giao tiếp nội bộ](./07-giao-tiep-noi-bo.md) | gRPC (sync) + RabbitMQ (async) |
| [08 — Quan sát hệ thống](./08-quan-sat-he-thong.md) | Prometheus, Loki, Tempo, Grafana |
| [09 — W3C Trace Context](./09-w3c-trace-context.md) | Distributed tracing qua HTTP và RabbitMQ |
| [10 — CI/CD Pipeline](./10-cicd-pipeline.md) | GitHub Actions build → test → push → deploy |
| [11 — Local Dev & Deploy](./11-local-dev-va-deploy.md) | Setup máy local, chạy monitoring, deploy production |
| [12 — Kiểm thử](./12-kiem-thu.md) | Strategy, stack, cách chạy tests |
| [13 — Thêm tính năng](./13-them-tinh-nang.md) | Checklist thêm endpoint, event, service mới |
| [14 — SignalR Realtime](./14-signalr-realtime.md) | Hub, envelope chuẩn, cách test từ frontend |
| [15 — Async Gateway](./15-async-gateway.md) | HTTP→Queue→Service, endpoints, consumers |
| [16 — Test Async Gateway](./16-test-async-gateway.md) | Testing guide cho các async endpoints |
| [17 — Async Flow & Grafana](./17-async-flow-va-grafana.md) | Luồng kỹ thuật đầy đủ, distributed trace, hướng dẫn Grafana |

---

## Kiến trúc một dòng

```
Browser / Mobile
       │  HTTP
       ▼
  nginx (port 5000)          ← API Gateway duy nhất ra ngoài
  ├── /auth/*  → AuthService
  ├── /orders/* → OrderService
  ├── /notifications/* → NotificationService
  └── /m01/*   → M01Service
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
| `http://localhost:5000` | API Gateway |
| `http://localhost:5000/auth/swagger` | Swagger AuthService |
| `http://localhost:5000/orders/swagger` | Swagger OrderService |
| `http://localhost:5000/m01/swagger` | Swagger M01Service |
| `http://localhost:15672` | RabbitMQ Management (guest/guest) |
| `http://localhost:3030` | Grafana (admin/admin) |
| `http://localhost:9090` | Prometheus |
