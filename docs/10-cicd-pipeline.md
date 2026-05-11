# 10 — CI/CD Pipeline

Mỗi lần push code lên GitHub, hệ thống tự động: chạy test → build Docker image → push lên registry → deploy lên server. Không cần SSH vào server để deploy thủ công.

---

## Tổng quan luồng

```
git push origin main
        │
        ▼
┌─────────────────────────────────────────┐
│                  CI                      │
│                                         │
│  detect-changes ──► resolve-services    │
│                              │          │
│                              ▼          │
│                          test (all)     │
│                              │          │
│                   ┌──────────┘          │
│                   ▼                     │
│        build-push (matrix: services)    │
│        → push image lên GHCR           │
└────────────────────┬────────────────────┘
                     │ (workflow_run event)
                     ▼
┌─────────────────────────────────────────┐
│                  CD                      │
│                                         │
│  nhánh dev  → deploy-staging (auto)     │
│  nhánh main → deploy-production (manual)│
└─────────────────────────────────────────┘
```

**Quan trọng:** Production không tự động deploy — phải bấm nút thủ công trên GitHub Actions. Đây là deliberate safety measure.

---

## CI Workflow (`.github/workflows/ci.yml`)

### Job 1: detect-changes

Dùng `dorny/paths-filter@v3` đọc `.github/path-filters.yml`:

```yaml
authservice:
  - "src/Services/AuthService/**"
  - "src/BuildingBlocks/**"

orderservice:
  - "src/Services/OrderService/**"
  - "src/BuildingBlocks/**"

notificationservice:
  - "src/Services/NotificationService/**"
  - "src/BuildingBlocks/**"

m01service:
  - "src/Services/M01Service/**"
  - "src/BuildingBlocks/**"
```

Output là JSON array: `["authservice","m01service"]` (chỉ những service có file thay đổi).

**Lý do:** Nếu chỉ sửa AuthService, không cần rebuild 3 service kia. Build thời gian từ 20 phút xuống còn 5 phút.

### Job 2: resolve-services

```yaml
if [ "${{ github.event_name }}" = "workflow_dispatch" ]; then
  # workflow_dispatch → build tất cả 4 services
  echo 'services=["authservice","orderservice","notificationservice","m01service"]'
else
  # push → chỉ build những service detect-changes trả ra
  CHANGED='${{ needs.detect-changes.outputs.changed }}'
```

`workflow_dispatch` luôn build tất cả — dùng khi cần force rebuild (ví dụ: sửa Dockerfile nhưng code không đổi, hoặc cần rollout lại sau incident).

### Job 3: test

Chạy `dotnet test` cho toàn bộ solution — không phân biệt service nào thay đổi:

```yaml
- run: dotnet restore
- run: dotnet build --no-restore --configuration Release
- run: dotnet test --no-build --configuration Release --logger "trx;LogFileName=results.trx"
- uses: actions/upload-artifact@v4  # Upload .trx file để xem kết quả test
  if: always()
  with:
    name: test-results
    path: "**/*.trx"
```

Test chạy song song với detect-changes (không có `needs` dependency). build-push chỉ bắt đầu khi cả test và resolve-services đều xong.

### Job 4: build-push

Matrix job — mỗi service build song song:

```yaml
strategy:
  fail-fast: false      # Một service fail không cancel các service khác
  matrix:
    service: ${{ fromJSON(needs.resolve-services.outputs.services) }}
```

Với mỗi service:

1. **Load config từ `services.json`:**
```json
{
  "authservice": {
    "dockerfile": "src/Services/AuthService/AuthService.API/Dockerfile",
    "context": "."
  }
}
```
`context: "."` — build context là root của repo vì Dockerfile cần copy `src/BuildingBlocks/` (shared code).

2. **Resolve image tag:**
```
nhánh main → prod-latest
nhánh dev  → dev-latest
```
Ngoài ra luôn có tag theo SHA commit: `ghcr.io/owner/hdos-authservice:abc1234`

3. **Build và push lên GHCR:**
```yaml
tags: |
  ghcr.io/${{ github.repository_owner }}/hdos-authservice:prod-latest
  ghcr.io/${{ github.repository_owner }}/hdos-authservice:${{ github.sha }}
```

Dùng GitHub Actions cache (`type=gha,scope=authservice`) để giảm thời gian build khi layers không thay đổi.

---

## CD Workflow (`.github/workflows/cd.yml`)

### Self-hosted runner

```yaml
runs-on: self-hosted
```

Tức là job chạy ngay trên server production/staging (đã cài GitHub Actions runner). Runner có quyền truy cập Docker daemon và các file `.env` trên server.

**Tại sao self-hosted?** Không cần mở SSH port từ bên ngoài. Server chủ động kết nối ra GitHub, nhận job rồi tự deploy. Bảo mật hơn nhiều so với GitHub-hosted runner SSH vào server.

### Deploy staging (tự động)

Trigger: CI trên nhánh `dev` hoàn thành thành công.

```yaml
on:
  workflow_run:
    workflows: [CI]
    types: [completed]
    branches: [dev]
```

```bash
# Pull images mới
docker compose \
  --env-file "${ENV_DIR}/.env" \          # /opt/hdos-staging/.env
  -f docker-compose.yml \
  -f docker-compose.server.yml \
  -f docker-compose.monitoring.yml \
  pull

# Restart với image mới
docker compose ... up -d --remove-orphans

# Xóa images cũ để tiết kiệm disk
docker image prune -f
```

`--remove-orphans` xóa container của service đã bị remove khỏi compose file.

### Deploy production (thủ công)

```yaml
on:
  workflow_dispatch:
    inputs:
      sha:
        description: "SHA cụ thể cần deploy (để trống = HEAD của main)"
```

Khi deploy production:
- Vào **GitHub → Actions → CD → Run workflow**
- Để trống SHA → deploy `prod-latest` (HEAD của main)
- Điền SHA cụ thể → deploy đúng commit đó (rollback bằng cách điền SHA cũ)

`environment: production` — GitHub Environments có thể cấu hình required reviewers (ai đó phải approve trước khi deploy).

---

## Cấu trúc file trên server

```
/opt/hdos-prod/           (/opt/hdos-staging/ cho staging)
├── .env                  ← compose substitution variables
│   # IMAGE_TAG=prod-latest
│   # GHCR_OWNER=hoanggggf
│   # ASPNETCORE_ENVIRONMENT=Production
│   # MSSQL_SA_PASSWORD=...
│   # JWT_SECRET=...
│
├── common.env            ← inject vào tất cả service containers
│   # Jwt__Secret=...
│   # RabbitMq__Host=rabbitmq
│   # OpenTelemetry__OtlpEndpoint=http://tempo:4317
│   # Loki__Uri=http://loki:3100
│
├── authservice.env       ← chỉ inject vào authservice container
│   # ConnectionStrings__AuthDb=Server=sqlserver,...
│
├── orderservice.env
│   # ConnectionStrings__OrderDb=...
│   # Services__Auth__GrpcUrl=http://authservice:8081
│
├── notificationservice.env
└── m01service.env
```

**Tại sao split thành nhiều file?** Principle of least privilege — orderservice không cần biết connection string của authservice database.

---

## docker-compose.server.yml

Override file dùng cho mọi server environment. Nó:
1. Thay `build:` thành `image:` (pull từ GHCR thay vì build local)
2. Inject `env_file` từ `${ENV_DIR}/`
3. Mount nginx config

```yaml
authservice:
  image: ghcr.io/${GHCR_OWNER}/hdos-authservice:${IMAGE_TAG}
  build: !reset null          # Xóa build config khỏi base compose
  env_file:
    - ${ENV_DIR}/common.env
    - ${ENV_DIR}/authservice.env
  environment:
    ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT}
```

`!reset null` là cú pháp Docker Compose v2 để xóa hoàn toàn key `build` từ base file. Nếu không có `!reset`, compose sẽ merge build và image — có thể gây hành vi không mong muốn.

---

## Thêm service vào CI/CD

1. **`services.json`** — thêm entry:
```json
"newservice": {
  "dockerfile": "src/Services/NewService/NewService.API/Dockerfile",
  "context": "."
}
```

2. **`.github/path-filters.yml`** — thêm filter:
```yaml
newservice:
  - "src/Services/NewService/**"
  - "src/BuildingBlocks/**"
```

3. **`docker-compose.server.yml`** — thêm service override:
```yaml
newservice:
  image: ghcr.io/${GHCR_OWNER}/hdos-newservice:${IMAGE_TAG}
  build: !reset null
  env_file:
    - ${ENV_DIR}/common.env
    - ${ENV_DIR}/newservice.env
  environment:
    ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT}
```

4. **Trên server** — tạo file env:
```bash
touch /opt/hdos-prod/newservice.env
# Điền ConnectionStrings__NewDb=...
```

5. Push lên `main` → `workflow_dispatch` với `force_build_all=true` để build lần đầu.

---

## Troubleshooting CI/CD

| Triệu chứng | Nguyên nhân | Fix |
|------------|------------|-----|
| build-push skip dù có code change | `path-filters.yml` không match đúng path | Kiểm tra path pattern trong path-filters.yml |
| deploy không cập nhật service mới | Service chưa có trong path-filters.yml | `workflow_dispatch` với force_build_all=true |
| `jq: parse error` trong Load service config | `services.json` invalid JSON (trailing comma, comment) | Validate JSON: `jq . services.json` |
| CD runner offline | Self-hosted runner service không chạy | `sudo systemctl start actions.runner.*` trên server |
| Image pull failed: unauthorized | GHCR_TOKEN hết hạn hoặc sai | Cập nhật secret `GHCR_TOKEN` trong GitHub repo settings |
| Container không restart sau deploy | `docker compose up` không detect image mới | `docker compose pull` trước rồi mới `up -d` |
