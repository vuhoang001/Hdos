#!/usr/bin/env bash
# demo-patient-daily-flow.sh
# ─────────────────────────────────────────────────────────────────────────────
# Demo end-to-end: Phase 4 auto-sync + Lakehouse DataContract `patient.daily.new`.
#
# Tạo 1 màn hình DynamicForm:
#   - Module:  clinical-${SUFFIX}
#   - Screen:  patient-daily-new  (Published)
#   - Tab:     "Tổng quan"
#   - 3 widget bind vào DataContract:
#       * KpiCard   — số bệnh nhân khoa đầu tiên (rows[0])
#       * BarChart  — bệnh nhân mới theo khoa
#       * PieChart  — tỉ trọng theo khoa
#
# KHÔNG cần migration ở DynamicForm — Phase 4 đã đẩy Provider+Operation
# `lakehouse::prefill` sẵn khi Lakehouse start. Script chỉ tạo screen + widget.
#
# Chạy:   bash scripts/demo-patient-daily-flow.sh
# Env:    BASE_URL  (default https://192.168.100.60:8443)
#         EMAIL     (default freshertest@hdos.local)
#         PASSWORD  (default Fresher@123)
#         FE_URL    (default https://192.168.100.60:4000)
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

BASE_URL="${BASE_URL:-https://192.168.100.60:8443}"
EMAIL="${EMAIL:-freshertest@hdos.local}"
PASSWORD="${PASSWORD:-Fresher@123}"
FULLNAME="${FULLNAME:-Fresher Tester}"
FE_URL="${FE_URL:-https://192.168.100.60:4000}"

SUFFIX="$(date +%s)"
MODULE_CODE="clinical-${SUFFIX}"
SCREEN_CODE="patient-daily-new"
CONTRACT_CODE="patient.daily.new"

bold()   { printf '\033[1m%s\033[0m\n' "$1"; }
hdr()    { printf '\n\033[1;34m▌ %s\033[0m\n' "$1"; }
ok()     { printf '\033[32m✓\033[0m %s\n' "$1"; }
warn()   { printf '\033[33m⚠\033[0m %s\n' "$1"; }
err()    { printf '\033[31m✗\033[0m %s\n' "$1" >&2; }
kv()     { printf '   \033[90m%-14s\033[0m %s\n' "$1" "$2"; }

for cmd in curl python3; do
    command -v "$cmd" >/dev/null || { err "Cần cài $cmd"; exit 1; }
done

CURL="curl -sk"

api() {
    local method="$1" path="$2" data="${3:-}"
    local http body tmp
    tmp="$(mktemp)"
    if [ -n "$data" ]; then
        http=$($CURL -o "$tmp" -w "%{http_code}" -X "$method" "${BASE_URL}${path}" \
            -H "Authorization: Bearer ${TOKEN:-}" \
            -H "Content-Type: application/json" \
            -d "$data")
    else
        http=$($CURL -o "$tmp" -w "%{http_code}" -X "$method" "${BASE_URL}${path}" \
            -H "Authorization: Bearer ${TOKEN:-}")
    fi
    body="$(cat "$tmp")"; rm -f "$tmp"
    if [ "$http" -ge 500 ]; then
        err "HTTP $http từ $method $path"
        echo "$body" | head -c 500 >&2
        exit 1
    fi
    echo "$body"
}

json_get() { python3 -c "import sys,json; d=json.load(sys.stdin); print($1)"; }

# ── 1. Login ─────────────────────────────────────────────────────────────────
hdr "1. Đăng nhập"
LOGIN_BODY=$(cat <<EOF
{"email":"${EMAIL}","password":"${PASSWORD}"}
EOF
)
resp="$(api POST /auth/login "$LOGIN_BODY")"
if ! echo "$resp" | grep -q '"success":true'; then
    warn "User chưa có → đăng ký"
    REG_BODY=$(cat <<EOF
{"email":"${EMAIL}","password":"${PASSWORD}","fullName":"${FULLNAME}"}
EOF
)
    api POST /auth/register "$REG_BODY" >/dev/null
    resp="$(api POST /auth/login "$LOGIN_BODY")"
fi
TOKEN=$(echo "$resp" | json_get "d['data']['token']")
ok "Login OK"

# ── 2. Verify Phase 4 auto-sync đã chạy ──────────────────────────────────────
hdr "2. Verify Phase 4: catalog lakehouse::prefill phải có sẵn"
ops_resp="$(api GET "/forms/admin/providers/lakehouse/operations")"
if echo "$ops_resp" | grep -q '"prefill"'; then
    ok "Operation lakehouse::prefill đã có (auto-sync OK)"
else
    err "Operation lakehouse::prefill KHÔNG có. Lakehouse chưa start hoặc sync fail."
    echo "$ops_resp" | head -c 500 >&2
    exit 1
fi

# ── 3. Smoke test contract endpoint ──────────────────────────────────────────
hdr "3. Smoke test /lakehouse/contracts/${CONTRACT_CODE}/prefill"
contract_resp="$(api GET "/lakehouse/contracts/${CONTRACT_CODE}/prefill?source=demo")"
row_count=$(echo "$contract_resp" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('rowCount', (d.get('data') or {}).get('rowCount', '?')))")
if [ "$row_count" = "?" ]; then
    err_msg=$(echo "$contract_resp" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('errorMessage','(không rõ)'))")
    warn "Endpoint chưa serve được — $err_msg"
    warn "→ Rebuild Lakehouse trên server: docker compose up -d --build lakehouseservice"
    warn "Tiếp tục tạo screen — chart sẽ trống cho tới khi rebuild."
elif [ "$row_count" = "8" ]; then
    ok "Contract trả về 8 row (OK)"
else
    warn "Row count = ${row_count} (kỳ vọng 8). Tiếp tục."
fi

# ── 4. Tạo Module + Screen ───────────────────────────────────────────────────
hdr "4. Tạo Module + Screen"
api POST /forms/admin/modules \
    "{\"code\":\"${MODULE_CODE}\",\"name\":\"Clinical Demo ${SUFFIX}\",\"description\":\"Demo PatientDaily DataContract\"}" >/dev/null
ok "Module $MODULE_CODE"

api POST /forms/admin/screens \
    "{\"moduleCode\":\"${MODULE_CODE}\",\"code\":\"${SCREEN_CODE}\",\"title\":\"Bệnh nhân mới trong ngày\",\"description\":\"Bind vào contract ${CONTRACT_CODE}\",\"sortOrder\":0}" >/dev/null
ok "Screen $SCREEN_CODE (Draft)"

# ── 5. Set DataSource lakehouse::prefill ─────────────────────────────────────
hdr "5. Bind DataSource → lakehouse::prefill"
api PUT "/forms/admin/screens/${MODULE_CODE}/${SCREEN_CODE}/data-sources" '[
  {"namespace":"patient","operationId":"lakehouse::prefill","requiredParams":["contractCode"]}
]' >/dev/null
ok "patient → lakehouse::prefill (required: contractCode)"

# ── 6. Tạo tab ───────────────────────────────────────────────────────────────
hdr "6. Tạo tab \"Tổng quan\""
tab_resp="$(api POST "/forms/admin/screens/${MODULE_CODE}/${SCREEN_CODE}/tabs" \
    '{"label":"Tổng quan","slug":"main","sortOrder":0,"isDefault":true}')"
TAB_ID=$(echo "$tab_resp" | json_get "d['data']['id']")
ok "Tab ID = $TAB_ID"

# ── 7. Save 3 widgets ────────────────────────────────────────────────────────
hdr "7. Save 3 widget (KpiCard + BarChart + PieChart)"
WIDGET_PAYLOAD=$(python3 <<'PY'
import json
widgets = [
    {
        "widgetKey":"kpi-first-dept","widgetType":"KpiCard",
        "gridX":0,"gridY":0,"gridW":8,"gridH":4,
        "configJson":json.dumps({
            "title":"Bệnh nhân mới — khoa đầu tiên",
            "valueExpression":"{{sources.patient.rows[0].newPatientCount}}",
            "unit":"người","color":"#7c3aed","icon":"users",
        }, ensure_ascii=False),
        "referenceId":None,
    },
    {
        "widgetKey":"bar-by-dept","widgetType":"BarChart",
        "gridX":8,"gridY":0,"gridW":16,"gridH":8,
        "configJson":json.dumps({
            "title":"Bệnh nhân mới theo khoa",
            "dataExpression":"{{sources.patient.rows}}",
            "labelField":"departmentName","valueField":"newPatientCount",
            "color":"#10b981",
        }, ensure_ascii=False),
        "referenceId":None,
    },
    {
        "widgetKey":"pie-by-dept","widgetType":"PieChart",
        "gridX":0,"gridY":8,"gridW":24,"gridH":8,
        "configJson":json.dumps({
            "title":"Tỉ trọng bệnh nhân mới theo khoa",
            "dataExpression":"{{sources.patient.rows}}",
            "labelField":"departmentName","valueField":"newPatientCount",
            "colors":["#7c3aed","#ec4899","#10b981","#f59e0b","#06b6d4","#3b82f6","#ef4444","#84cc16"],
        }, ensure_ascii=False),
        "referenceId":None,
    },
]
print(json.dumps(widgets, ensure_ascii=False))
PY
)
api PUT "/forms/admin/screens/${MODULE_CODE}/${SCREEN_CODE}/tabs/${TAB_ID}/widgets" "$WIDGET_PAYLOAD" >/dev/null
ok "Đã lưu 3 widget"

# ── 8. Publish ───────────────────────────────────────────────────────────────
hdr "8. Publish screen"
api POST "/forms/admin/screens/${MODULE_CODE}/${SCREEN_CODE}/publish" "" >/dev/null
ok "Screen Published"

# ── 9. Verify layout endpoint ────────────────────────────────────────────────
hdr "9. Verify layout (BE đã resolve baseUrl + resourcePath)"
LAYOUT_JSON="$(api GET "/forms/screens/${MODULE_CODE}/${SCREEN_CODE}/layout")"
ds_info=$(echo "$LAYOUT_JSON" | python3 <<'PY'
import json, sys
d = json.load(sys.stdin).get('data', {})
sources = d.get('dataSources', [])
if not sources:
    print('NO_SOURCES')
else:
    s = sources[0]
    print(f"ns={s.get('namespace')} kind={s.get('kind')} baseUrl={s.get('baseUrl')} path={s.get('resourcePath')}")
PY
)
ok "DataSource resolved: $ds_info"

# ── 10. Tóm tắt + URL FE ─────────────────────────────────────────────────────
hdr "10. ✅ XONG"
kv "Module"     "$MODULE_CODE"
kv "Screen"     "$SCREEN_CODE"
kv "Contract"   "$CONTRACT_CODE"

URL="${FE_URL}/client?module=${MODULE_CODE}&screen=${SCREEN_CODE}&contractCode=${CONTRACT_CODE}"
echo ""
bold "🌐 Mở URL FE:"
echo "   $URL"
echo ""
warn "Lưu ý: FE useDataSources hiện CHỈ substitute {contractCode} placeholder."
warn "       Param khác (source, date, department) chưa được forward — DemoSource là duy nhất → vẫn pick auto."
echo ""
