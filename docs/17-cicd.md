# 17 — CI/CD với GitHub Actions

## 1. Tổng quan luồng

```
git push main
     │
     ▼
┌─────────────────────────────────────────────────────┐
│  CI (ci.yml)                                        │
│                                                     │
│  detect-changes ──► chỉ service nào thay đổi       │
│       │                                             │
│       ├──► test        ← toàn bộ dotnet test        │
│       │                                             │
│       └──► build-push  ← build song song, push GHCR │
└────────────────────────┬────────────────────────────┘
                         │ CI pass
                         ▼
┌─────────────────────────────────────────────────────┐
│  CD (cd.yml)                                        │
│                                                     │
│  SSH → Ubuntu server                                │
│    docker compose pull   ← kéo image mới từ GHCR   │
│    docker compose up -d  ← restart container thay đổi│
└─────────────────────────────────────────────────────┘
```

**Điểm quan trọng:** Pipeline chỉ build những service thực sự thay đổi.
Nếu bạn chỉ sửa `AuthService`, chỉ `authservice` image được rebuild. Tiết kiệm
30-40 phút build nếu có nhiều service.

---

## 2. Cấu trúc file CI/CD

```
.github/
├── path-filters.yml        ← Khai báo file nào → service nào
└── workflows/
    ├── ci.yml              ← Build + Test + Push image lên GHCR
    └── cd.yml              ← SSH vào server, deploy

services.json               ← Dockerfile path của từng service
docker-compose.prod.yml     ← Override image sang GHCR (dùng ở server)
scripts/setup-server.sh     ← Cài Docker + chuẩn bị Ubuntu server
```

---

## 3. GitHub Secrets cần thiết

Vào **Settings → Secrets and variables → Actions** trong GitHub repo, thêm:

| Secret | Giá trị | Dùng ở |
|---|---|---|
| `SERVER_HOST` | IP hoặc domain Ubuntu server | cd.yml |
| `SERVER_USER` | SSH user (thường `ubuntu`) | cd.yml |
| `SSH_PRIVATE_KEY` | Nội dung file `~/.ssh/id_rsa` | cd.yml |
| `GHCR_USER` | GitHub username của bạn | cd.yml |
| `GHCR_TOKEN` | GitHub PAT với quyền `read:packages` | cd.yml |

> `GITHUB_TOKEN` (dùng để push image trong CI) được tự động cấp bởi GitHub,
> không cần tạo thủ công.

---

## 4. Chuẩn bị Ubuntu server lần đầu

```bash
# 1. SSH vào server
ssh ubuntu@<SERVER_IP>

# 2. Chạy script setup (cài Docker)
curl -fsSL https://raw.githubusercontent.com/<your-org>/Hdos/main/scripts/setup-server.sh | sudo bash

# 3. Copy file cấu hình lên server
scp docker-compose.yml docker-compose.prod.yml ubuntu@<SERVER_IP>:/opt/hdos/

# 4. Tạo .env trên server
cat > /opt/hdos/.env << 'EOF'
JWT_SECRET=your-real-secret-min-32-chars
GHCR_OWNER=hoanggggf
EOF
```

---

## 5. Docker images được lưu ở đâu?

Images được push lên **GitHub Container Registry (GHCR)**:

```
ghcr.io/hoanggggf/hdos-authservice:latest
ghcr.io/hoanggggf/hdos-authservice:<git-sha>    ← tag theo commit, có thể rollback
ghcr.io/hoanggggf/hdos-orderservice:latest
ghcr.io/hoanggggf/hdos-orderservice:<git-sha>
...
```

---

## 6. Rollback về version cũ

Trên server, chỉ cần đổi tag image và restart:

```bash
cd /opt/hdos

# Xem lịch sử các commit SHA đã deploy
docker images ghcr.io/hoanggggf/hdos-authservice

# Rollback authservice về commit cũ
JWT_SECRET=... GHCR_OWNER=hoanggggf \
  docker compose -f docker-compose.yml -f docker-compose.prod.yml \
  up -d authservice
```

Hoặc trigger thủ công trong **GitHub → Actions → CD → Run workflow**.

---

## 7. Trigger deploy thủ công

1. Vào GitHub repo → tab **Actions**
2. Chọn workflow **CD**
3. Nhấn **Run workflow** → chọn nhánh `main`

Dùng khi cần hotfix khẩn hoặc rollback.

---

## 8. Xem log build

- **CI log:** GitHub → Actions → CI → click vào run đang chạy
- **CD log:** GitHub → Actions → CD
- **Server log:** `docker compose -f docker-compose.yml -f docker-compose.prod.yml logs -f <service-name>`
