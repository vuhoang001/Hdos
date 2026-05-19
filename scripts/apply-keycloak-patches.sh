#!/usr/bin/env bash
# Áp dụng các thay đổi Keycloak lên instance đang chạy.
# Idempotent — chạy nhiều lần không bị lỗi.
#
# Dùng khi: realm đã tồn tại nên --import-realm không tự cập nhật.
# Chạy trên server sau khi deploy:
#   bash scripts/apply-keycloak-patches.sh [KEYCLOAK_URL] [ADMIN_PASSWORD]
#
# Mặc định:
#   KEYCLOAK_URL     = http://localhost:8080
#   ADMIN_PASSWORD   = Admin1234!

set -euo pipefail

KC_URL="${1:-http://localhost:8080}"
KC_PASS="${2:-Admin1234!}"
REALM="hdos"

echo "=== Keycloak patch: $KC_URL/realms/$REALM ==="

# ── Lấy admin token ──────────────────────────────────────────────────────────
ADMIN_TOKEN=$(curl -sf -X POST "$KC_URL/realms/master/protocol/openid-connect/token" \
  -d "client_id=admin-cli&grant_type=password&username=admin&password=$KC_PASS" \
  | grep -o '"access_token":"[^"]*"' | cut -d'"' -f4)

[ -z "$ADMIN_TOKEN" ] && { echo "ERROR: Không lấy được admin token"; exit 1; }
echo "✓ Admin token OK"

auth() { echo "-H \"Authorization: Bearer $ADMIN_TOKEN\""; }

# ── Helper: GET JSON ─────────────────────────────────────────────────────────
kc_get() { curl -sf -H "Authorization: Bearer $ADMIN_TOKEN" "$KC_URL/admin/realms/$REALM/$1"; }

# ── Helper: POST JSON ────────────────────────────────────────────────────────
kc_post() {
  local path="$1"; local body="$2"
  HTTP_CODE=$(curl -s -o /tmp/kc_out.json -w "%{http_code}" \
    -X POST \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H "Content-Type: application/json" \
    "$KC_URL/admin/realms/$REALM/$path" \
    -d "$body")
  echo "$HTTP_CODE"
}

# ────────────────────────────────────────────────────────────────────────────
# PATCH 1: Thêm audience mapper "hdos-backend-audience" vào hdos-frontend
# ────────────────────────────────────────────────────────────────────────────
echo ""
echo "--- Patch 1: hdos-frontend → audience mapper hdos-backend ---"

CLIENT_UUID=$(kc_get "clients?clientId=hdos-frontend" \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print(d[0]['id'])" 2>/dev/null)

[ -z "$CLIENT_UUID" ] && { echo "ERROR: Không tìm thấy client hdos-frontend"; exit 1; }
echo "  Client UUID: $CLIENT_UUID"

# Kiểm tra mapper đã tồn tại chưa
EXISTING=$(kc_get "clients/$CLIENT_UUID/protocol-mappers/models" \
  | python3 -c "
import sys, json
mappers = json.load(sys.stdin)
names = [m['name'] for m in mappers]
print('found' if 'hdos-backend-audience' in names else 'not_found')
" 2>/dev/null)

if [ "$EXISTING" = "found" ]; then
  echo "  ✓ Mapper đã tồn tại, bỏ qua"
else
  CODE=$(kc_post "clients/$CLIENT_UUID/protocol-mappers/models" '{
    "name": "hdos-backend-audience",
    "protocol": "openid-connect",
    "protocolMapper": "oidc-audience-mapper",
    "consentRequired": false,
    "config": {
      "included.client.audience": "hdos-backend",
      "id.token.claim": "false",
      "access.token.claim": "true"
    }
  }')
  [ "$CODE" = "201" ] && echo "  ✓ Mapper tạo thành công (201)" \
                       || echo "  ERROR: HTTP $CODE — $(cat /tmp/kc_out.json)"
fi

# ────────────────────────────────────────────────────────────────────────────
# Thêm patch mới bên dưới theo cùng pattern
# ────────────────────────────────────────────────────────────────────────────

echo ""
echo "=== Tất cả patches áp dụng xong ==="
