---
title: How to setup CI/CD
sidebar_position: 5
description: Thiết lập GitHub Actions CI/CD để tự động build, test và deploy lên Ubuntu server.
tags: [how-to, cicd, github-actions, docker]
---

# How-to — Setup CI/CD với GitHub Actions

## Điều kiện

- GitHub repo (public hoặc private)
- Ubuntu server (VPS) với SSH access
- Docker chưa cài → chạy `scripts/setup-server.sh`

---

## Bước 1 — Chuẩn bị Ubuntu server

```bash
# SSH vào server lần đầu
ssh ubuntu@<SERVER_IP>

# Cài Docker
curl -fsSL https://raw.githubusercontent.com/<your-org>/Hdos/main/scripts/setup-server.sh | sudo bash

# Tạo thư mục deploy
mkdir -p /opt/hdos
```

Copy file cấu hình lên server:

```bash
# Từ máy local
scp docker-compose.yml docker-compose.prod.yml ubuntu@<SERVER_IP>:/opt/hdos/
```

Tạo `.env` trên server:

```bash
# Trên server
cat > /opt/hdos/.env << 'EOF'
JWT_SECRET=your-real-secret-min-32-chars
GHCR_OWNER=hoanggggf
EOF
```

---

## Bước 2 — Tạo GitHub Secrets

Vào **GitHub repo → Settings → Secrets and variables → Actions → New repository secret**:

| Tên | Giá trị |
|---|---|
| `SERVER_HOST` | IP hoặc domain Ubuntu server |
| `SERVER_USER` | SSH user (thường `ubuntu`) |
| `SSH_PRIVATE_KEY` | Nội dung `~/.ssh/id_rsa` của máy local |
| `GHCR_USER` | GitHub username |
| `GHCR_TOKEN` | GitHub PAT với quyền `read:packages` |

> Tạo PAT tại: GitHub → Settings → Developer settings → Personal access tokens → Fine-grained.
> Chỉ cần quyền **Packages: Read**.

---

## Bước 3 — Cấp quyền đọc GHCR cho server

Trên Ubuntu server, đăng nhập GHCR một lần để test:

```bash
echo "<GHCR_TOKEN>" | docker login ghcr.io -u <GHCR_USER> --password-stdin
```

---

## Bước 4 — Push lên main và kiểm tra

```bash
git push origin main
```

Vào **GitHub → Actions** để theo dõi:
1. Workflow **CI** chạy: detect → test → build → push image
2. Workflow **CD** chạy kế tiếp: SSH vào server → pull → up

---

## Kiểm tra deploy thành công

```bash
# Trên server
docker compose -f docker-compose.yml -f docker-compose.prod.yml ps

# Xem log
docker compose -f docker-compose.yml -f docker-compose.prod.yml logs -f apigateway
```

---

## Trigger deploy thủ công

Vào **GitHub → Actions → CD → Run workflow** → chọn nhánh `main`.

---

## Rollback

```bash
# Trên server — xem danh sách image đã pull
docker images ghcr.io/hoanggggf/hdos-authservice

# Sửa docker-compose.prod.yml đổi tag từ :latest sang :<git-sha>
# Rồi chạy lại
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d authservice
```
