---
title: CI/CD Architecture
sidebar_position: 5
description: Sơ đồ và giải thích toàn bộ pipeline CI/CD — file nào làm gì, secret nào cần thiết.
tags: [reference, cicd, github-actions]
---

# Reference — CI/CD Architecture

## Sơ đồ pipeline

```
git push main
     │
     ▼
 ci.yml ──────────────────────────────────────────────────────────────
 │                                                                    │
 │  Job 1: detect-changes                                            │
 │    dorny/paths-filter đọc .github/path-filters.yml               │
 │    Output: ["authservice", "orderservice"]  ← chỉ service thay đổi│
 │                                                                    │
 │  Job 2: test (song song với detect)                               │
 │    dotnet restore → build → test                                  │
 │                                                                    │
 │  Job 3: build-push  (matrix = changed services, chạy song song)  │
 │    Đọc dockerfile path từ services.json                           │
 │    docker buildx build → push ghcr.io/.../hdos-<service>:latest  │
 │    docker buildx build → push ghcr.io/.../hdos-<service>:<sha>   │
 └────────────────────────────────────────────────────────────────────
                              │ (CI pass trên main)
                              ▼
 cd.yml ──────────────────────────────────────────────────────────────
 │                                                                    │
 │  appleboy/ssh-action SSH vào SERVER_HOST                          │
 │    cd /opt/hdos                                                   │
 │    docker login ghcr.io                                           │
 │    docker compose pull        ← kéo image mới                    │
 │    docker compose up -d       ← restart container thay đổi       │
 │    docker image prune -f      ← dọn image cũ                     │
 └────────────────────────────────────────────────────────────────────
```

---

## File CI/CD và vai trò

| File | Vai trò |
|---|---|
| `.github/workflows/ci.yml` | Build, test, push image. Chạy trên mọi push/PR |
| `.github/workflows/cd.yml` | Deploy lên server. Chỉ chạy sau CI pass trên `main` |
| `.github/path-filters.yml` | Map service → path. Quyết định service nào rebuild |
| `services.json` | Map service → dockerfile path |
| `docker-compose.prod.yml` | Override image sang GHCR cho production |
| `scripts/setup-server.sh` | Cài Docker lên Ubuntu lần đầu |
| `.github/CODEOWNERS` | Gán reviewer tự động theo thư mục |

---

## GitHub Secrets

| Secret | Dùng ở | Mô tả |
|---|---|---|
| `GITHUB_TOKEN` | ci.yml | Tự động cấp. Push image lên GHCR |
| `SERVER_HOST` | cd.yml | IP hoặc domain Ubuntu server |
| `SERVER_USER` | cd.yml | SSH user (ubuntu / root) |
| `SSH_PRIVATE_KEY` | cd.yml | Nội dung private key SSH |
| `GHCR_USER` | cd.yml | GitHub username |
| `GHCR_TOKEN` | cd.yml | PAT với quyền `read:packages` |

---

## Image naming convention

```
ghcr.io/<owner>/hdos-<service>:latest     ← luôn là bản mới nhất
ghcr.io/<owner>/hdos-<service>:<git-sha>  ← tag cố định, dùng để rollback
```

Ví dụ:
```
ghcr.io/hoanggggf/hdos-authservice:latest
ghcr.io/hoanggggf/hdos-authservice:7621e9c
```

---

## Docker Compose files trên server

Server luôn chạy với 2 file:

```bash
docker compose \
  -f docker-compose.yml \          # base: network, volumes, env vars, healthchecks
  -f docker-compose.prod.yml \     # override: image → GHCR, env → Production
  up -d
```

`docker-compose.override.yml` (tự động load khi dev local) **không** có trên server.

---

## Khi nào CI build lại service?

| Thay đổi | Service rebuild |
|---|---|
| `src/Services/AuthService/**` | authservice |
| `src/BuildingBlocks/**` | tất cả service dùng BuildingBlocks |
| `src/ApiGateway/**` | apigateway |
| `docker-compose.yml` | không rebuild (CI không watch file này) |
| `.github/path-filters.yml` | không rebuild (workflow infra) |

---

## CODEOWNERS — Quản lý ownership

```
src/Services/AuthService/    @developer-a   # PR vào đây → tự ping @developer-a
src/BuildingBlocks/          @lead          # Shared code → lead review bắt buộc
.github/                     @lead          # CI/CD config → chỉ lead sửa
```

Kích hoạt: **Settings → Branches → main → Require review from Code Owners**.
