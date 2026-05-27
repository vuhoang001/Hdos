# 11 — Local Dev và Deploy

---

## Yêu cầu môi trường

| Tool | Version | Kiểm tra |
|------|---------|---------|
| .NET SDK | 8.0+ | `dotnet --version` |
| Docker Desktop / Docker Engine | 24+ | `docker --version` |
| Docker Compose | v2 (plugin) | `docker compose version` |

---

## Chạy local

### 1. Clone và start dependencies

```bash
git clone https://github.com/<owner>/Hdos.git
cd Hdos

# Khởi động toàn bộ infrastructure (SQL Server, RabbitMQ, nginx)
docker compose up -d sqlserver rabbitmq nginx
```

SQL Server cần ~30 giây để start. Kiểm tra: `docker compose ps` — sqlserver phải `healthy`.

Khi `authservice` chạy lần đầu, nó tự migrate `AuthDb` + seed user `admin@hdos.dev / Admin1234!` + `testuser@hdos.dev / Test1234!` (xem [06 — Xác thực](./06-xac-thuc.md)).

### 2. Chạy services

**Option A: Docker Compose (đơn giản)**
```bash
docker compose up -d
```
Tất cả services build và chạy trong container. Truy cập qua `http://localhost:5000`.

**Option B: dotnet run (debug)**
```bash
# Terminal 1 — AuthService
cd src/Services/AuthService/AuthService.API
dotnet run

# Terminal 2 — OrderService
cd src/Services/OrderService/OrderService.API
dotnet run

# Các service còn lại tương tự
```

Khi chạy local bằng `dotnet run`, services lắng nghe trên port riêng (xem `launchSettings.json`). nginx vẫn proxy qua container name `authservice` — nên nếu muốn dùng nginx gateway local, cần chạy trong container.

**Option C: Kết hợp (phổ biến nhất)**
```bash
# Chạy tất cả trong container
docker compose up -d

# Sau đó restart service cần debug (vẫn dùng image, không hot-reload)
docker compose restart authservice
```

Để hot-reload khi code thay đổi, dùng `dotnet watch run` trực tiếp — nhưng service sẽ ra ngoài Docker network, cần điều chỉnh connection strings.

### 3. Kiểm tra

```bash
# Health check
curl http://localhost:5000/health

# Gateway info
curl http://localhost:5000/

# RabbitMQ Management UI
open http://localhost:15672  # guest/guest

# Swagger của từng service
open http://localhost:5000/auth/swagger
open http://localhost:5000/orders/swagger
open http://localhost:5000/notifications/swagger
open http://localhost:5000/m01/swagger
```

### 4. Monitoring (optional)

```bash
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d
open http://localhost:3030  # Grafana admin/admin
```

---

## Environment Variables (local)

`docker-compose.yml` đã hardcode giá trị default cho dev:

```yaml
MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD:-Hdos!DevPass123}"
JWT_SECRET: "${JWT_SECRET:-hdos-dev-secret-change-me-in-production-!!}"
POSTGRES_DM_PASSWORD: "${POSTGRES_DM_PASSWORD:-dm_pass}"   # DataMatchingService
```

Nếu muốn override, tạo file `.env` ở root:

```bash
# .env (gitignored)
MSSQL_SA_PASSWORD=MyDevPassword123!
JWT_SECRET=my-local-secret
POSTGRES_DM_PASSWORD=my-dm-pass
```

**Không commit file `.env`** — đã có trong `.gitignore`.

---

## Database Migrations

EF Core Code-First. Migrations tự động apply khi service khởi động:

```csharp
// Program.cs
await app.MigrateDbAsync();  // Gọi context.Database.MigrateAsync()
```

**Tạo migration mới:**
```bash
cd src/Services/AuthService/AuthService.Infrastructure

dotnet ef migrations add <MigrationName> \
  --project . \
  --startup-project ../AuthService.API \
  --output-dir Persistence/Migrations
```

**Apply thủ công:**
```bash
dotnet ef database update \
  --project . \
  --startup-project ../AuthService.API \
  --connection "Server=localhost,1433;Database=AuthDb;User Id=sa;Password=Hdos!DevPass123;TrustServerCertificate=True"
```

---

## Cấu trúc file cần biết khi dev

```
Hdos/
├── docker-compose.yml          ← Base cho local dev
├── docker-compose.server.yml   ← Override cho server (staging/prod)
├── docker-compose.monitoring.yml ← Thêm Prometheus, Loki, Tempo, Grafana
├── services.json               ← Map service → Dockerfile (dùng bởi CI)
├── nginx/
│   └── nginx.conf              ← Sửa file này để thêm route, CORS, etc.
├── monitoring/
│   ├── prometheus.yml          ← Thêm scrape job khi có service mới
│   ├── loki.yml
│   ├── tempo.yml
│   └── grafana/provisioning/   ← Datasources + dashboards auto-load
└── src/
    ├── BuildingBlocks/         ← Shared code (Common/Contracts)
    └── Services/
        └── <ServiceName>/
            ├── <Service>.API/
            │   ├── Dockerfile
            │   └── appsettings.json
            └── ...
```

---

## Deploy lên server (Production)

### Lần đầu setup server

```bash
# 1. Cài Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker ubuntu

# 2. Tạo thư mục env
sudo mkdir -p /opt/hdos-prod
sudo chown ubuntu:ubuntu /opt/hdos-prod

# 3. Tạo .env (compose substitution variables)
cat > /opt/hdos-prod/.env << 'EOF'
IMAGE_TAG=prod-latest
GHCR_OWNER=hoanggggf
ASPNETCORE_ENVIRONMENT=Production
MSSQL_SA_PASSWORD=<strong-password>
JWT_SECRET=<long-random-secret>
ENV_DIR=/opt/hdos-prod
EOF

# 4. Tạo common.env (inject vào tất cả containers)
cat > /opt/hdos-prod/common.env << 'EOF'
Jwt__Secret=<same-jwt-secret>
Jwt__Issuer=Hdos.Auth
Jwt__Audience=Hdos.Services
Jwt__ExpiresMinutes=60
RabbitMq__Host=rabbitmq
RabbitMq__Port=5672
OpenTelemetry__OtlpEndpoint=http://tempo:4317
Loki__Uri=http://loki:3100
EOF

# 5. Tạo service-specific env files
cat > /opt/hdos-prod/authservice.env << 'EOF'
ConnectionStrings__AuthDb=Server=sqlserver,1433;Database=AuthDb;User Id=sa;Password=<password>;TrustServerCertificate=True;Encrypt=False
EOF

cat > /opt/hdos-prod/orderservice.env << 'EOF'
ConnectionStrings__OrderDb=Server=sqlserver,1433;Database=OrderDb;User Id=sa;Password=<password>;TrustServerCertificate=True;Encrypt=False
Services__Auth__GrpcUrl=http://authservice:8081
EOF

cat > /opt/hdos-prod/notificationservice.env << 'EOF'
ConnectionStrings__NotificationDb=Server=sqlserver,1433;Database=NotificationDb;User Id=sa;Password=<password>;TrustServerCertificate=True;Encrypt=False
EOF

cat > /opt/hdos-prod/m01service.env << 'EOF'
ConnectionStrings__M01Db=Server=sqlserver,1433;Database=M01Db;User Id=sa;Password=<password>;TrustServerCertificate=True;Encrypt=False
EOF

cat > /opt/hdos-prod/datamatchingservice.env << 'EOF'
ConnectionStrings__DataMatchingDb=Host=postgres-dm;Port=5432;Database=DataMatchingDb;Username=dm_user;Password=<postgres-dm-password>
Matching__WorkerIntervalSeconds=30
EOF
```

Và thêm vào `/opt/hdos-prod/.env`:
```bash
POSTGRES_DM_PASSWORD=<postgres-dm-password>
```

### Cài GitHub Actions self-hosted runner

```bash
# Trên GitHub: Settings → Actions → Runners → New self-hosted runner
# Chọn Linux x64, làm theo hướng dẫn GitHub hiển thị:

mkdir actions-runner && cd actions-runner
curl -o actions-runner-linux-x64.tar.gz -L https://github.com/actions/runner/releases/download/...
tar xzf actions-runner-linux-x64.tar.gz
./config.sh --url https://github.com/<owner>/Hdos --token <TOKEN>

# Cài service để tự khởi động
sudo ./svc.sh install
sudo ./svc.sh start
```

Runner chạy như systemd service, tự restart khi reboot.

### Deploy lần đầu

```bash
# SSH vào server
ssh ubuntu@192.168.100.60

# Clone repo (runner cần file docker-compose)
cd ~
git clone https://github.com/<owner>/Hdos.git

# Login GHCR
echo <GHCR_TOKEN> | docker login ghcr.io -u <username> --password-stdin

# Pull và start
cd Hdos
docker compose \
  --env-file /opt/hdos-prod/.env \
  -f docker-compose.yml \
  -f docker-compose.server.yml \
  -f docker-compose.monitoring.yml \
  up -d
```

### Deploy sau đó (CI/CD tự làm)

Sau khi setup runner, mọi deploy sẽ tự động qua GitHub Actions (xem [10 — CI/CD Pipeline](./10-cicd-pipeline.md)).

Để deploy production thủ công: **GitHub → Actions → CD → Run workflow**.

---

## Cập nhật nginx config trên server

nginx dùng volume mount nên không cần rebuild image:

```bash
# SSH vào server
cd ~/Hdos
git pull

# Kiểm tra syntax
docker exec hdos-nginx nginx -t

# Reload (không downtime)
docker exec hdos-nginx nginx -s reload

# Nếu thêm upstream mới (cần restart):
docker restart hdos-nginx
```

> **Lưu ý:** Container nginx có `container_name: hdos-nginx` (không có suffix `-1`). Dùng tên này trong mọi lệnh `docker exec`/`docker restart`.

---

## Logs và Debugging

```bash
# Log của service cụ thể
docker logs hdos-authservice-1 --tail 100 -f

# Log tất cả services
docker compose logs -f

# Vào container
docker exec -it hdos-authservice-1 /bin/sh

# Kiểm tra network connectivity
docker exec hdos-orderservice-1 curl http://authservice:8080/health

# Kiểm tra RabbitMQ queues
open http://server-ip:15672  # guest/guest

# Xem trace trong Grafana
open http://server-ip:3030
```

---

## Ports

| Port (host) | Service | Notes |
|------------|---------|-------|
| 5000 | nginx (API Gateway) | Entry point duy nhất cho client |
| 1433 | SQL Server | Chỉ expose trong dev, đóng trên prod |
| 5433 | PostgreSQL (DataMatchingService) | Chỉ expose trong dev, đóng trên prod |
| 5672 | RabbitMQ AMQP | Dùng bởi services |
| 15672 | RabbitMQ Management | Đóng trên prod |
| 3030 | Grafana | Admin UI |
| 9090 | Prometheus | Metrics |
| 3100 | Loki | Log API (không expose bên ngoài) |
| 3200 | Tempo | Trace API (không expose bên ngoài) |

**Lưu ý production:** Chỉ mở port 5000 (và 22 cho SSH) ra ngoài firewall. Các port internal (RabbitMQ, MSSQL, Grafana) chỉ truy cập từ local network hoặc VPN.
