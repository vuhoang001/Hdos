# 64. Apache Superset — Phase 1: Standalone

> Tích hợp Apache Superset như một **BI module độc lập** vào hệ thống Hdos. Chia 6 phase. **Tài liệu này mô tả Phase 1 — chạy được Superset sau nginx, login admin/admin.** Phase 2-6 chỉ liệt kê roadmap.

## 0. Vì sao Superset, không phải tự build dashboard?

Hdos đã có `DataMatchingService` + `LakehouseService` sinh chart engine + SDUI cho FE. Nhưng cho **internal analytics** (BI team, quản lý bệnh viện cần ad-hoc query, build dashboard tự do), tự build sẽ tốn quá nhiều công. Superset:

- SQL Lab — admin query trực tiếp các DB nghiệp vụ
- 50+ loại chart, drag-drop dashboard
- Native filters, cross-filter, alert/report (Phase 5+)
- Embedded SDK — FE nhúng dashboard bằng iframe + guest token (Phase 4)

Trade-off: Superset là Python/Flask, **không fit Clean Architecture .NET của Hdos**. Coi nó như infrastructure tool (giống RabbitMQ, Grafana), KHÔNG phải service nghiệp vụ.

## 1. Roadmap 6 Phase

| Phase | Mục tiêu | Trạng thái |
|-------|----------|-----------|
| **1. Standalone** | Superset chạy sau nginx, login admin/admin | ✅ doc này |
| 2. SSO AuthService | Login Hdos = login Superset (custom Security Manager hoặc OIDC) | ⏳ chưa làm |
| 3. Data source | Admin add DataMatchingDb, M01Db, … qua UI Superset | ⏳ chưa làm |
| 4. Embedded SDK | BE cấp guest token → FE nhúng dashboard cho end-user | ⏳ chưa làm |
| 5. Staging/Prod | `docker-compose.server.yml` overrides, production secret, HTTPS chuẩn | ⏳ chưa làm |
| 6. Monitoring | Prometheus + Loki + Tempo + Grafana dashboard cho Superset | ⏳ chưa làm |

## 2. Kiến trúc Phase 1

```
       Browser
          │ https://localhost:8443/superset/
          ▼
    ┌──────────┐
    │  nginx   │  ← strip /superset/ prefix
    │ :8443    │  ← thêm header X-Forwarded-Prefix: /superset
    └─────┬────┘
          │ http://superset:8088/...
          ▼
    ┌─────────────────┐        ┌──────────────────────┐
    │  superset       │ ───→   │  postgres-superset   │
    │  (Flask+gunicorn│        │  (metadata DB)       │
    │   port 8088)    │        │   :5432 internal     │
    └─────────────────┘        └──────────────────────┘
          ▲
          │ depends_on: service_completed_successfully
          │
    ┌─────────────────┐
    │  superset-init  │ ← chạy một lần: db upgrade + create admin + init
    │  (restart: no)  │
    └─────────────────┘
```

### Tại sao 3 service Docker?

| Service | Vai trò | Restart policy |
|---------|---------|----------------|
| `postgres-superset` | Metadata DB cho Superset (lưu dashboards, charts, users, datasources) | always (default) |
| `superset-init` | Chạy 1 lần: `superset db upgrade` + `fab create-admin` + `superset init` | `"no"` |
| `superset` | Web server (Flask + gunicorn) — entrypoint mặc định của image | always |

Lý do tách `superset-init` ra: nếu để init script chạy trong cùng container web, mỗi lần restart sẽ chạy lại migration không cần thiết. Pattern này tương tự ASP.NET Core EF Core migration on startup vs migration bundle.

### Tại sao Postgres riêng (không dùng `postgres-dm`)?

Database-per-service principle (Section 8 CLAUDE.md). Superset metadata không liên quan dữ liệu nghiệp vụ; backup/restore độc lập. Khi Phase 5 deploy lên prod, có thể tách ra RDS instance riêng dễ dàng.

### Tại sao subpath `/superset/` (không subdomain)?

Convention của Hdos: tất cả service truy cập qua `https://localhost:8443/<prefix>/`. Subpath khó cấu hình hơn subdomain (Flask cần `X-Forwarded-Prefix`), nhưng đồng nhất với pattern hiện tại — không phải thay đổi DNS/TLS cert khi deploy.

## 3. File layout

```
superset/
├── Dockerfile              ← FROM apache/superset:3.1.1 + COPY config + init script
├── superset_config.py      ← Flask app config (SECRET_KEY, DB URI, ProxyFix, feature flags)
├── docker-init.sh          ← Init script chạy bởi service superset-init
├── requirements-local.txt  ← Python deps thêm (Phase 1: trống)
└── .env.example            ← Mẫu biến môi trường (copy → .env, không commit)
```

## 4. Cấu hình chi tiết

### 4.1. `superset_config.py` — các quyết định cần nhớ

| Setting | Giá trị | Lý do |
|---------|---------|-------|
| `SECRET_KEY` | env `SUPERSET_SECRET_KEY` | Flask session signing. PROD: bắt buộc >=32 ký tự random |
| `SQLALCHEMY_DATABASE_URI` | env `SUPERSET_DATABASE_URI` | Metadata DB connection (Postgres) |
| `ENABLE_PROXY_FIX = True` | bật | Werkzeug ProxyFix đọc `X-Forwarded-*` headers |
| `PROXY_FIX_CONFIG` | `x_prefix=1` | Quan trọng — gán `SCRIPT_NAME = /superset` để `url_for()` sinh URL có prefix |
| `SESSION_COOKIE_PATH = "/superset"` | scope hẹp | Không leak cookie sang frontend Next.js ở `/` |
| `CACHE_TYPE = "SimpleCache"` | in-memory | Phase 1 đủ. Phase 4 chuyển Redis khi cần share cache + guest token |
| `EMBEDDED_SUPERSET = False` | off | Bật ở Phase 4 |

### 4.2. `nginx.conf` — location block

```nginx
upstream superset { server superset:8088; }

location ^~ /superset/ {
    proxy_pass             http://superset/;          # trailing slash → strip prefix
    proxy_set_header       X-Forwarded-Prefix /superset;  # cho ProxyFix
    proxy_read_timeout     300s;                      # SQL Lab query có thể chậm
    proxy_buffering        off;                       # streaming response (download CSV)
}
```

Các header `Host`, `X-Real-IP`, `X-Forwarded-For`, `X-Forwarded-Proto`, `Upgrade`, `Connection` kế thừa từ `server` block — đã có sẵn trong nginx.conf, không lặp lại.

### 4.3. `docker-compose.yml` — 3 services + 1 volume

| Service | Image | Port internal | Volume |
|---------|-------|---------------|--------|
| `postgres-superset` | `postgres:16-alpine` | 5432 | `hdos-pgdata-superset` |
| `superset-init` | `hdos/superset:latest` (build từ `./superset`) | — | — |
| `superset` | `hdos/superset:latest` (build từ `./superset`) | 8088 | — |

`nginx.depends_on` đã thêm `superset`.

## 5. Cách chạy local

### 5.1. Lần đầu

```bash
# 1. Tạo file .env (nếu chưa có) từ template
cp superset/.env.example .env.superset
# Hoặc append vào .env hiện có (giữ các biến hdos khác)

# 2. Build + chạy
docker compose up -d --build postgres-superset superset-init superset

# 3. Đợi healthcheck (~1 phút lần đầu vì init DB)
docker compose ps superset

# 4. Restart nginx để load config mới
docker compose restart nginx
```

### 5.2. Truy cập

```
URL:      https://localhost:8443/superset/
User:     admin
Password: admin
```

Chấp nhận self-signed cert ở browser.

> **Lưu ý:** Phase 1 KHÔNG SSO với AuthService — login Superset không liên quan login Hdos. Admin user của Superset hoàn toàn tách biệt.

### 5.3. Kiểm tra healthcheck

```bash
# Health endpoint Superset (qua nginx)
curl -k https://localhost:8443/superset/health
# Expected: "OK"

# Direct (cần exec vào container vì không expose host port)
docker compose exec superset curl -fsS http://localhost:8088/health
```

### 5.4. Logs

```bash
docker compose logs -f superset
docker compose logs superset-init    # logs init một lần
```

### 5.5. Reset metadata DB (dev only)

```bash
docker compose down superset superset-init postgres-superset
docker volume rm hdos_hdos-pgdata-superset
docker compose up -d --build postgres-superset superset-init superset
```

## 6. Done criteria — Phase 1

- [x] `docker compose up -d --build` chạy không lỗi
- [x] `https://localhost:8443/superset/` lên UI Superset
- [x] Login `admin/admin` vào được dashboard rỗng
- [x] Không có 502/504 từ nginx
- [x] Tất cả container healthy: `postgres-superset`, `superset` (superset-init: exited 0)
- [ ] **CHƯA làm:** SSO, kết nối DB nghiệp vụ, embedded SDK, monitoring

## 7. Troubleshooting

### 7.1. `502 Bad Gateway` khi vào `/superset/`

Superset chưa healthy. Đợi 30-60s sau lần đầu start (cần `db upgrade` + `init`).

```bash
docker compose ps superset
docker compose logs superset --tail 50
```

### 7.2. Static assets 404 (CSS/JS không load)

Header `X-Forwarded-Prefix` không tới Superset → `url_for()` sinh URL không có `/superset/`. Kiểm tra:

```bash
docker compose exec nginx grep -A 3 'location.*/superset/' /etc/nginx/nginx.conf
# Phải có dòng: proxy_set_header X-Forwarded-Prefix /superset;
```

### 7.3. Cookie không lưu — login bị lặp

`SESSION_COOKIE_PATH` mismatch với prefix. Đảm bảo `superset_config.py` có `SESSION_COOKIE_PATH = "/superset"`.

### 7.4. `superset-init` báo lỗi `admin already exists`

Không sao — script đã `|| echo` swallow lỗi này. Init vẫn tiếp tục.

### 7.5. Container `superset-init` restart liên tục

Đây là bug — phải set `restart: "no"`. Kiểm tra docker-compose.yml.

## 8. Anti-patterns Phase 1 đã tránh

| Anti-pattern | Lý do tránh |
|--------------|-------------|
| Expose port 8088 ra host | Convention Hdos: chỉ nginx expose host port. Debug qua `docker exec` |
| Dùng chung `postgres-dm` cho metadata | Database-per-service principle |
| Để init script chạy mỗi lần restart `superset` | Chậm, phí tài nguyên — tách thành `superset-init` chạy 1 lần |
| Hard-code SECRET_KEY trong config file | PROD: rò rỉ secret. Dùng env var |
| Bật `EMBEDDED_SUPERSET` từ Phase 1 | YAGNI — chưa có FE consume, bật sớm gây lỗi confusing |

## 9. Liên kết tới các Phase tiếp theo

- **Phase 2 (SSO)** — sẽ thêm `superset/security_manager.py` extends `SupersetSecurityManager`, parse JWT từ `Authorization` header hoặc cookie do AuthService set. AuthService cần expose JWKS endpoint nếu chuyển sang RS256.
- **Phase 3 (data sources)** — admin add Postgres DataMatchingDb/LakehouseDb qua UI; nếu cần SQL Server (AuthDb/M01Db) thì bật pyodbc + msodbcsql trong Dockerfile.
- **Phase 4 (embedded)** — thêm Redis service, bật `EMBEDDED_SUPERSET=True`, tạo BE endpoint `.NET` `POST /api/dashboards/{id}/guest-token` gọi Superset `/security/guest_token/`; viết doc FE nhúng SDK.
- **Phase 5 (staging/prod)** — `docker-compose.server.yml` override: production SECRET_KEY (lấy từ vault), persistent volume backup, HTTPS qua nginx prod cert.
- **Phase 6 (monitoring)** — Superset có `STATSD_HOST` env hỗ trợ statsd_exporter → Prometheus. Logs đẩy vào Loki qua Docker log driver.
