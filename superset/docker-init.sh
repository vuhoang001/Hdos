#!/bin/bash
# ── Hdos Superset init (chỉ chạy trong service superset-init) ─────────────
# 1. Upgrade metadata DB schema
# 2. Create admin user nếu chưa có; nếu đã có → reset password theo env var
#    (đồng bộ DB với SUPERSET_ADMIN_PASSWORD hiện tại — tránh lệch password)
# 3. Init Superset roles & permissions
set -e

ADMIN_USER="${SUPERSET_ADMIN_USERNAME:-admin}"
ADMIN_PASS="${SUPERSET_ADMIN_PASSWORD:-admin}"
ADMIN_EMAIL="${SUPERSET_ADMIN_EMAIL:-admin@hdos.local}"

echo "[hdos-superset-init] superset db upgrade"
superset db upgrade

echo "[hdos-superset-init] superset fab create-admin (user=${ADMIN_USER})"
if superset fab create-admin \
    --username "${ADMIN_USER}" \
    --firstname Superset \
    --lastname Admin \
    --email "${ADMIN_EMAIL}" \
    --password "${ADMIN_PASS}"; then
    echo "[hdos-superset-init] admin user created"
else
    echo "[hdos-superset-init] admin exists — resetting password from env"
    superset fab reset-password --username "${ADMIN_USER}" --password "${ADMIN_PASS}"
fi

echo "[hdos-superset-init] superset init"
superset init

echo "[hdos-superset-init] done"
