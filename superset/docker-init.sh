#!/bin/bash
# ── Hdos Superset init (chỉ chạy trong service superset-init) ─────────────
# 1. Upgrade metadata DB schema
# 2. Create admin user (idempotent — fab báo lỗi nếu đã tồn tại, swallow)
# 3. Init Superset roles & permissions
set -e

echo "[hdos-superset-init] superset db upgrade"
superset db upgrade

echo "[hdos-superset-init] superset fab create-admin"
superset fab create-admin \
    --username "${SUPERSET_ADMIN_USERNAME:-admin}" \
    --firstname Superset \
    --lastname Admin \
    --email "${SUPERSET_ADMIN_EMAIL:-admin@hdos.local}" \
    --password "${SUPERSET_ADMIN_PASSWORD:-admin}" \
    || echo "[hdos-superset-init] admin user may already exist — continuing"

echo "[hdos-superset-init] superset init"
superset init

echo "[hdos-superset-init] done"
