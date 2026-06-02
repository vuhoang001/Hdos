# 10 — CI/CD Pipeline

Code lưu trên **GitHub**. CI/CD chạy trên **GitLab** (GitLab mirror GitHub repo tự động). Mỗi lần push lên GitHub, GitLab kéo code về, chạy build → push image lên GitLab Registry → deploy container lên server. Không cần SSH vào server để deploy thủ công.

---

## Tổng quan luồng

```
git push origin main
        │
        ▼
   GitHub repo
        │  (mirror tự động — webhook)
        ▼
   GitLab repo
        │
        ▼
┌─────────────────────────────────────┐
│           GitLab CI (build)          │
│                                     │
│  build 7 services song song         │
│  → push image lên GitLab Registry   │
│    registry.gitlab.com/project/     │
│    hdos-SERVICE:prod-latest          │
└──────────────┬──────────────────────┘
               │
               ▼
┌─────────────────────────────────────┐
│           GitLab CD (deploy)         │
│                                     │
│  nhánh staging → deploy-staging     │
│  nhánh main    → deploy-production  │
└─────────────────────────────────────┘
               │
               ▼
        Server (self-hosted runner)
        docker compose pull + up -d
```

---

## Setup GitLab Mirror (làm một lần)

### Bước 1: Tạo GitLab project và mirror từ GitHub

1. Vào GitLab → New project → **Import project** → chọn GitHub
2. Hoặc tạo project trống → **Settings → Repository → Mirroring repositories**
3. Điền:
   - **Git repository URL:** `https://github.com/<owner>/<repo>.git`
   - **Mirror direction:** Pull
   - **Authentication:** GitHub Personal Access Token (cần `repo` scope)
4. Bật **Trigger pipelines for mirror updates**

### Bước 2: Thêm webhook GitHub → GitLab (trigger ngay khi push)

Mặc định GitLab mirror 5 phút/lần. Để trigger ngay khi push:

1. Vào GitLab project → **Settings → Repository → Mirroring** → copy **Trigger URL**
2. Vào GitHub repo → **Settings → Webhooks → Add webhook**
   - Payload URL: URL vừa copy từ GitLab
   - Content type: `application/json`
   - Event: **Just the push event**

Từ đây, mỗi lần push lên GitHub → GitHub gọi webhook → GitLab sync code ngay → pipeline chạy.

### Bước 3: Thêm tag cho GitLab runner

```bash
sudo nano /etc/gitlab-runner/config.toml
# Thêm: tags = ["self-hosted"]
sudo gitlab-runner restart
```

### Bước 4: Cấu hình GitLab CI/CD Variables

Vào GitLab project → **Settings → CI/CD → Variables**, thêm:

| Variable | Mô tả | Protected |
|----------|-------|-----------|
| `JWT_SECRET` | JWT signing key, tối thiểu 32 ký tự | Yes |
| `MSSQL_SA_PASSWORD` | SQL Server SA password | Yes |
| `POSTGRES_DM_PASSWORD` | PostgreSQL DataMatching password | Yes |
| `POSTGRES_DF_PASSWORD` | PostgreSQL DynamicForm password | Yes |

`CI_REGISTRY_IMAGE`, `CI_REGISTRY_USER`, `CI_REGISTRY_PASSWORD` — GitLab tự inject, không cần thêm.

---

## GitLab CI Pipeline (`.gitlab-ci.yml`)

### Stage 1: build

Matrix build — 7 service chạy song song:

```yaml
parallel:
  matrix:
    - SERVICE:
        - authservice
        - orderservice
        - notificationservice
        - m01service
        - asyncgateway
        - datamatchingservice
        - dynamicformservice
```

Mỗi service:
1. Đọc `Dockerfile` và `context` từ `services.json`
2. Resolve tag:
   - `main` → `prod-latest`
   - `staging` → `dev-latest`
3. Build và push 2 tag lên GitLab Registry:
   ```
   registry.gitlab.com/project/hdos-authservice:prod-latest
   registry.gitlab.com/project/hdos-authservice:<SHA>
   ```

### Stage 2: deploy

| Job | Trigger | Môi trường |
|-----|---------|-----------|
| `deploy-staging` | push lên `staging` | `/opt/hdos-staging` |
| `deploy-production` | push lên `main` | `/opt/hdos-prod` |

Script deploy:
```bash
REGISTRY_IMAGE=$CI_REGISTRY_IMAGE   # registry.gitlab.com/project
IMAGE_TAG=prod-latest               # hoặc dev-latest

docker compose \
  --env-file /opt/hdos-prod/.env \
  -f docker-compose.yml \
  -f docker-compose.server.yml \
  -f docker-compose.monitoring.yml \
  pull

docker compose ... down --remove-orphans --timeout 30
docker compose ... up -d
docker image prune -f
```

Sau 15 giây, kiểm tra container nào ở trạng thái `Exit` hoặc `Restarting` → fail pipeline nếu có.

---

## Cấu trúc file trên server

```
/opt/hdos-prod/           (/opt/hdos-staging/ cho staging)
├── .env                  ← compose substitution variables
│   REGISTRY_IMAGE=registry.gitlab.com/project
│   IMAGE_TAG=prod-latest
│   MSSQL_SA_PASSWORD=...
│   POSTGRES_DM_PASSWORD=...
│   POSTGRES_DF_PASSWORD=...
│   JWT_SECRET=...
│   ASPNETCORE_ENVIRONMENT=Production
│
├── common.env            ← inject vào tất cả service containers
│   Jwt__Secret=...
│   RabbitMq__Host=rabbitmq
│   OpenTelemetry__OtlpEndpoint=http://tempo:4317
│   Loki__Uri=http://loki:3100
│
├── authservice.env
│   ConnectionStrings__AuthDb=Server=sqlserver,...
│
├── orderservice.env
│   ConnectionStrings__OrderDb=...
│   Services__Auth__GrpcUrl=http://authservice:8081
│
├── notificationservice.env
├── m01service.env
├── asyncgateway.env
├── datamatchingservice.env
└── dynamicformservice.env
```

---

## docker-compose.server.yml

Override file dùng chung cho mọi server environment. Dùng biến generic `${REGISTRY_IMAGE}` và `${IMAGE_TAG}` — không phụ thuộc vào GitLab hay GitHub:

```yaml
authservice:
  image: ${REGISTRY_IMAGE}/hdos-authservice:${IMAGE_TAG}
  build: !reset null
  env_file:
    - ${ENV_DIR}/common.env
    - ${ENV_DIR}/authservice.env
  environment:
    ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT}
```

`!reset null` xóa key `build` từ base compose file, tránh conflict giữa `build` và `image`.

| CI | `REGISTRY_IMAGE` | `IMAGE_TAG` |
|----|-----------------|-------------|
| GitLab | `$CI_REGISTRY_IMAGE` | `prod-latest` |
| GitHub Actions (thủ công) | `ghcr.io/$GHCR_USER` | commit SHA |

---

## GitHub Actions (dự phòng, chỉ chạy thủ công)

File `.github/workflows/ci.yml` và `cd.yml` vẫn giữ nguyên nhưng **trigger đã đổi thành `workflow_dispatch` only** — không auto-run khi push.

Dùng khi nào: cần deploy khẩn từ GitHub mà GitLab runner đang offline.

Kích hoạt lại auto-run: thêm lại trigger `push`/`pull_request` vào `ci.yml` và `workflow_run` vào `cd.yml`.

---

## Thêm service mới vào CI/CD

1. **`services.json`** — thêm entry:
```json
"newservice": {
  "dockerfile": "src/Services/NewService/NewService.API/Dockerfile",
  "context": "."
}
```

2. **`.gitlab-ci.yml`** — thêm service vào matrix:
```yaml
- SERVICE:
    - ...
    - newservice
```

3. **`docker-compose.server.yml`** — thêm override:
```yaml
newservice:
  image: ${REGISTRY_IMAGE}/hdos-newservice:${IMAGE_TAG}
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

5. Push lên `main` → pipeline tự chạy.

---

## Troubleshooting

| Triệu chứng | Nguyên nhân | Fix |
|------------|------------|-----|
| Pipeline không chạy sau khi push GitHub | Webhook chưa cấu hình hoặc mirror chưa sync | Kiểm tra GitLab → Settings → Repository → Mirroring → bấm sync thủ công |
| Runner không nhận job | Tag `self-hosted` chưa có hoặc runner chưa start | `sudo gitlab-runner status`, kiểm tra tag trong config.toml |
| Image pull failed: unauthorized | Runner chưa login GitLab Registry | Kiểm tra `before_script: docker login` trong `.gitlab-ci.yml` |
| `jq: parse error` | `services.json` invalid JSON | `jq . services.json` để validate |
| Container Exit/Restarting sau deploy | Biến env thiếu hoặc sai | Kiểm tra file `.env` trên server |
| `REGISTRY_IMAGE` trống trong compose | Biến chưa được export | Kiểm tra `variables:` trong deploy job của `.gitlab-ci.yml` |
