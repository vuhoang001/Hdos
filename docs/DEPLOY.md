# Hướng Dẫn Deploy — Hdos

## Tổng quan luồng CI/CD

```
Push code lên nhánh dev
       │
       ▼
  [GitHub Actions: CI]
  ├─ Detect changed services
  ├─ dotnet test
  └─ Build & Push Docker images → ghcr.io (tag: dev-latest + SHA)
       │
       ▼  (CI pass)
  [GitHub Actions: CD — Staging]  ← tự động
  └─ Self-hosted runner SSH vào server staging
     docker compose pull + up -d

Push lên main → CI build + tag prod-latest
       │
       ▼  (bấm nút thủ công)
  [GitHub Actions: CD — Production]  ← thủ công
  └─ Self-hosted runner SSH vào server production
     docker compose pull + up -d
```

---

## 1. Chuẩn bị server (chạy một lần duy nhất)

### 1.1 Cài Docker

Chạy script có sẵn với quyền sudo:

```bash
sudo bash scripts/setup-server.sh
```

Script này sẽ cài Docker CE, Docker Compose plugin, và tạo thư mục `/opt/hdos`.

### 1.2 Cài GitHub Actions self-hosted runner

Vào repo GitHub → **Settings → Actions → Runners → New self-hosted runner**, chọn Linux, làm theo hướng dẫn. Runner này sẽ là máy thực thi lệnh deploy.

Sau khi cài xong, bật runner tự khởi động cùng hệ thống:

```bash
cd ~/actions-runner
sudo ./svc.sh install
sudo ./svc.sh start
```

### 1.3 Tạo cấu trúc thư mục env trên server

**Staging:**
```bash
sudo mkdir -p /opt/hdos-staging
```

**Production:**
```bash
sudo mkdir -p /opt/hdos-prod
```

Cấu trúc mỗi thư mục:
```
/opt/hdos-staging/          (hoặc /opt/hdos-prod/)
├── .env                    ← biến cho docker compose substitution
├── common.env              ← biến dùng chung cho tất cả service
├── authservice.env
├── orderservice.env
├── notificationservice.env
├── m01service.env
└── apigateway.env
```

---

## 2. Điền file .env trên server

### 2.1 File `.env` (docker compose substitution)

```bash
sudo nano /opt/hdos-staging/.env
```

```env
IMAGE_TAG=dev-latest
GHCR_OWNER=hoanggggf
ASPNETCORE_ENVIRONMENT=Development
ENV_DIR=/opt/hdos-staging
```

Với production:
```env
IMAGE_TAG=prod-latest
GHCR_OWNER=hoanggggf
ASPNETCORE_ENVIRONMENT=Production
ENV_DIR=/opt/hdos-prod
```

### 2.2 File `common.env` (biến dùng chung)

```bash
sudo nano /opt/hdos-staging/common.env
```

```env
# Sinh mật khẩu mạnh: openssl rand -base64 32
MSSQL_SA_PASSWORD=<mật_khẩu_mạnh_ở_đây>

ConnectionStrings__AuthDb=Server=sqlserver,1433;Database=AuthDb;User Id=sa;Password=<mật_khẩu>;TrustServerCertificate=True;Encrypt=False
ConnectionStrings__OrderDb=Server=sqlserver,1433;Database=OrderDb;User Id=sa;Password=<mật_khẩu>;TrustServerCertificate=True;Encrypt=False
ConnectionStrings__NotificationDb=Server=sqlserver,1433;Database=NotificationDb;User Id=sa;Password=<mật_khẩu>;TrustServerCertificate=True;Encrypt=False
ConnectionStrings__M01Db=Server=sqlserver,1433;Database=M01Db;User Id=sa;Password=<mật_khẩu>;TrustServerCertificate=True;Encrypt=False

RabbitMq__Host=rabbitmq
RabbitMq__Port=5672

# Sinh JWT secret: openssl rand -base64 48
Jwt__Secret=<chuỗi_ngẫu_nhiên_ít_nhất_32_ký_tự>
Jwt__Issuer=Hdos.Auth
Jwt__Audience=Hdos.Services
Jwt__ExpiresMinutes=60
```

### 2.3 Các file env riêng từng service

**`authservice.env`:**
```env
Kestrel__RestPort=8080
Kestrel__GrpcPort=8081
```

**`orderservice.env`:**
```env
Services__Auth__GrpcUrl=http://authservice:8081
```

**`notificationservice.env`**, **`m01service.env`**, **`apigateway.env`:** tạo file rỗng hoặc thêm biến riêng nếu cần.

```bash
sudo touch /opt/hdos-staging/notificationservice.env
sudo touch /opt/hdos-staging/m01service.env
sudo touch /opt/hdos-staging/apigateway.env
```

---

## 3. Cấu hình GitHub Secrets & Variables

Vào repo GitHub → **Settings → Secrets and variables → Actions**.

### 3.1 Secrets

| Tên | Giá trị |
|-----|---------|
| `GHCR_TOKEN` | GitHub PAT với quyền `read:packages` |

> Tạo PAT: GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens → chọn quyền `read:packages`.

### 3.2 Variables (theo environment)

Tạo 2 environments trong GitHub: `staging` và `production`.

**Environment `staging`:**

| Tên | Giá trị |
|-----|---------|
| `GHCR_USER` | `hoanggggf` |
| `DEPLOY_ENV_FILE` | `/opt/hdos-staging/.env` |

**Environment `production`:**

| Tên | Giá trị |
|-----|---------|
| `GHCR_USER` | `hoanggggf` |
| `DEPLOY_ENV_FILE` | `/opt/hdos-prod/.env` |

---

## 4. Chạy Local (Development)

### 4.1 Chỉ chạy app (không monitoring)

```bash
# Sao chép file env
cp .env.example .env

# Build và khởi động toàn bộ stack
docker compose up -d --build

# Xem log
docker compose logs -f
```

API Gateway chạy tại: `http://localhost:5000`

### 4.2 Chạy kèm monitoring (Prometheus + Loki + Tempo + Grafana)

```bash
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d --build
```

| Service | URL |
|---------|-----|
| API Gateway | http://localhost:5000 |
| Grafana | http://localhost:3000 (admin/admin) |
| Prometheus | http://localhost:9090 |
| RabbitMQ Management | http://localhost:15672 |

### 4.3 Tắt stack

```bash
docker compose down

# Tắt và xoá luôn data volumes
docker compose down -v
```

---

## 5. Deploy Staging (tự động)

Staging deploy xảy ra **tự động** khi:
1. Có code được push lên nhánh `dev`
2. CI pipeline (test + build + push image) **pass thành công**

Không cần làm gì thêm — CD workflow sẽ tự pull image mới và restart service trên server staging.

**Theo dõi tiến trình:** GitHub → Actions → tab "CD" → job "Deploy → Staging (auto)"

---

## 6. Deploy Production (thủ công)

Production **không tự động** — phải bấm nút thủ công.

### Bước 1: Merge code vào `main`

```bash
git checkout main
git merge dev
git push origin main
```

CI sẽ chạy và build image với tag `prod-latest`.

### Bước 2: Kích hoạt deploy

1. Vào GitHub → **Actions → CD workflow**
2. Bấm **"Run workflow"**
3. Điền SHA cụ thể nếu muốn deploy một commit cụ thể (để trống = HEAD của main)
4. Bấm **"Run workflow"** (nút xanh)

### Bước 3: Kiểm tra sau deploy

```bash
# SSH vào server production
ssh user@your-server

# Kiểm tra container đang chạy
docker compose -f /path/to/docker-compose.yml ps

# Xem log của một service cụ thể
docker logs hdos-apigateway --tail 50
docker logs hdos-authservice --tail 50
```

---

## 7. Rollback

Nếu deploy lỗi, rollback bằng cách deploy lại SHA cũ:

1. Vào GitHub → **Actions → CD workflow**
2. Bấm **"Run workflow"**
3. Điền SHA của commit muốn rollback về vào ô `sha`
4. Bấm chạy

Hoặc rollback thủ công trên server:

```bash
# SSH vào server
docker compose \
  -f docker-compose.yml \
  -f docker-compose.server.yml \
  --env-file /opt/hdos-prod/.env \
  pull  # kéo image theo tag trong .env

docker compose \
  -f docker-compose.yml \
  -f docker-compose.server.yml \
  --env-file /opt/hdos-prod/.env \
  up -d --remove-orphans
```

---

## 8. Xử lý sự cố thường gặp

### Container không khởi động được

```bash
# Xem log container
docker logs hdos-<tên_service> --tail 100

# Xem trạng thái tất cả container
docker compose ps
```

### SQL Server chưa ready, service bị lỗi kết nối

SQL Server cần ~30 giây để khởi động lần đầu. Kiểm tra health:

```bash
docker inspect hdos-sqlserver | grep -A5 Health
```

### Image không pull được từ GHCR

Đảm bảo `GHCR_TOKEN` secret còn hiệu lực và có quyền `read:packages`. Kiểm tra bằng cách login thủ công:

```bash
echo "<token>" | docker login ghcr.io -u hoanggggf --password-stdin
```

### Runner offline

```bash
# SSH vào server, khởi động lại runner
sudo systemctl restart actions.runner.*
sudo systemctl status actions.runner.*
```

---

## 9. Sinh secret an toàn

```bash
# Mật khẩu SQL Server
openssl rand -base64 24 | tr -d '=' | tr '+/' 'Az'

# JWT Secret (tối thiểu 32 ký tự)
openssl rand -base64 48
```
