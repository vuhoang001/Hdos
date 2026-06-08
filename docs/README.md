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
| [05 — Nginx Gateway & HTTPS/TLS](./05-nginx-gateway.md) | Reverse proxy, routing theo prefix, CORS, self-signed cert dev, production cert |
| [06 — Xác thực, Phân quyền & License](./06-xac-thuc.md) | Custom JWT HS256, RBAC, permission claims, license embed vào JWT, admin API gán/revoke |
| [07 — Giao tiếp nội bộ](./07-giao-tiep-noi-bo.md) | gRPC (sync) + RabbitMQ (async) |
| [08 — Quan sát hệ thống](./08-quan-sat-he-thong.md) | Prometheus, Loki, Tempo, Grafana, W3C distributed tracing |
| [10 — CI/CD Pipeline](./10-cicd-pipeline.md) | GitHub Actions build → test → push → deploy |
| [11 — Local Dev & Deploy](./11-local-dev-va-deploy.md) | Setup máy local, chạy monitoring, deploy production |
| [12 — Kiểm thử](./12-kiem-thu.md) | Strategy, stack, cách chạy tests |
| [13 — Thêm tính năng](./13-them-tinh-nang.md) | Checklist thêm endpoint, event, service mới |
| [14 — SignalR Realtime](./14-signalr-realtime.md) | Hub, envelope chuẩn, cách test từ frontend |
| [15 — Async Gateway](./15-async-gateway.md) | HTTP→Queue→Service, endpoints, test guide, Grafana observability |
| [17 — MassTransit Messaging](./17-masstransit-messaging.md) | MassTransit + RabbitMQ: topology, naming, cách thêm event, Transactional Outbox Pattern, External Consumer Pattern |
| [22 — CDC với Debezium + Kafka](./22-cdc-debezium-kafka.md) | Change Data Capture: SQL Server CDC → Debezium → Kafka → .NET consumer; Adapter Ingest Gateway (nhận data từ external) |
| [23 — DataMatchingService](./23-data-matching-service.md) | Ingest, deduplication, matching records từ nhiều source system |
| [24 — Dashboard & SSE Push](./24-dashboard-data-matching.md) | Dashboard engine: config-driven, SDUI sections, REST API; SSE push realtime (MatchingWorker → Frontend) |
| [25 — Server-Driven UI](./25-sdui-server-driven-ui.md) | SDUI engine: server quyết định layout, client chỉ render |
| [28 — Message Payload Standard](./28-message-payload-standard.md) | Quy chuẩn cấu trúc message theo CloudEvents (CNCF): `IntegrationEvent` cho internal, `ExternalMessage` cho external |
| [29 — DynamicFormService](./29-dynamic-form-service.md) | Tổng quan, domain model, API đầy đủ (BDUI forms + SDUI screens), pages, widget catalog, luồng hoạt động |
| [32 — DynamicFormService Spec](./32-dynamic-form-spec.md) | Technical spec: enums, value objects, entities, business rules, validation rules chi tiết |
| [33 — Screen Designer](./33-screen-designer.md) | Business rules, validation, state machine riêng cho Screen Designer / Canvas |
| [34 — Widget Catalog](./34-widget-catalog.md) | Danh mục 31 widget templates (visualization, healthcare, filter, layout, ai) |
| [40 — Schema Discovery](./40-schema-discovery.md) | Endpoint `/.../schema` cho DataMatching + Lakehouse → FE hiện dropdown DataBinding thay vì gõ tay; auto-mapping by name |
| [41 — Loose Coupling Architecture](./41-loose-coupling-architecture.md) | Provider/Operation Catalog: tách bạch Producer / Catalog / Consumer; FE không hardcode URL; Admin chọn dropdown thay vì gõ resourcePath |
| [42 — Admin API Refactor](./42-admin-api-refactor.md) | Tách AdminFormsController + xóa AdminPagesController (duplicate Screens). Thêm Module CRUD (Update/Delete) với cascade guard |
| [43 — Warehouse Sync → DataMatching](./43-warehouse-sync-to-lakehouse.md) | Pattern pull data từ DW external (Postgres/SQL Server) qua `WarehouseViewSyncer` → publish `RawRecordIngestRequestedIntegrationEvent` vào DataMatching. Phân chia trách nhiệm DE (VIEW SQL) vs BE (BackgroundService C#) + mô phỏng end-to-end. **Updated cho Phase 2 (doc 44).** |
| [44 — Unified Ingest Pipeline](./44-unified-ingest-pipeline.md) | Hợp nhất DataMatching + LakehouseSnapshot thành 1 pipeline: mọi source (HIS / BHYT / lakehouse view / API ngoài) publish 1 event → DataMatching apply SourceProfile mapping → `/dm/records/{id}` thống nhất cho FE. ViewBinding registry + migration từ Phase 1 |
| [45 — Lakehouse Auto-Enroll SourceProfile](./45-lakehouse-auto-sourceprofile.md) | Phase 2.5 — khi admin tạo ViewBinding, LakehouseService introspect schema view (Npgsql information_schema), suggest mapping snake→Pascal, gọi DataMatching enroll SourceProfile + tạo binding trong 1 call. Hướng B (auto MVP) + C (preview + confirm) với code mẫu + cross-service HTTP client |
| [46 — Playbook: Thêm Nguồn Data và Hiển Thị FE](./46-playbook-add-source-data.md) | Playbook thực hành step-by-step copy-paste-được cho admin / dev mới: 2 cách thêm 1 nguồn data + hiển thị qua DynForm. Cách 1 (push REST/file cho HIS/BHYT), Cách 2 (lakehouse view auto-enroll qua MVP B). Có bảng so sánh, pitfalls, ví dụ onboard nguồn mới |
| [47 — Test MVP B Lakehouse View](./47-test-mvp-b-lakehouse-view.md) | Test guide end-to-end cho `POST /lakehouse/view-bindings/with-auto-profile` (MVP B): inspect schema view, gọi auto-profile, verify SourceProfile + ViewBinding + sync. Có case study bed_occupancy + 5 view khác |
| [48 — FE Guide: Consume /dm/pages Chart](./48-frontend-consume-dm-pages-chart-guide.md) | Hướng dẫn FE lấy dữ liệu chart-ready từ `GET /dm/pages/{code}` (SduiEngine): reference catalog (endpoint, response shape, 5 component types, TS types) + hands-on FE implementation (Next.js + Recharts + auto-refresh). Bridge từ lakehouse → with-auto-profile → sync → chart |
| [00 — Hướng dẫn viết Spec cho AI](./00-spec-format.md) | Format chuẩn để viết technical spec đủ rõ cho AI implement |

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
