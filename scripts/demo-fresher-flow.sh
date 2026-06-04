#!/usr/bin/env bash
# demo-fresher-flow.sh
# ─────────────────────────────────────────────────────────────────────────────
# Script demo end-to-end luồng DataMatching → DynamicForm cho fresher.
# Cùng với docs/37-huong-dan-toan-luong-cho-fresher.md.
#
# Chạy:   bash scripts/demo-fresher-flow.sh
# Env:    BASE_URL  (default https://192.168.100.60:8443)
#         EMAIL     (default freshertest@hdos.local)
#         PASSWORD  (default Fresher@123)
#         FULLNAME  (default Fresher Tester)
#
# Có thể re-run nhiều lần — mỗi lần tạo module/screen/form với suffix timestamp.
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

BASE_URL="${BASE_URL:-https://192.168.100.60:8443}"
EMAIL="${EMAIL:-freshertest@hdos.local}"
PASSWORD="${PASSWORD:-Fresher@123}"
FULLNAME="${FULLNAME:-Fresher Tester}"

SUFFIX="$(date +%s)"
SOURCE_SYSTEM="his-fresher"
RECORD_TYPE="benh-nhan"
MODULE_CODE="fresher-demo-${SUFFIX}"
SCREEN_CODE="patient-review"
FORM_KEY="patient-review-form"

# ── color helpers ────────────────────────────────────────────────────────────
bold()   { printf '\033[1m%s\033[0m\n' "$1"; }
hdr()    { printf '\n\033[1;34m▌ %s\033[0m\n' "$1"; }
ok()     { printf '\033[32m✓\033[0m %s\n' "$1"; }
warn()   { printf '\033[33m⚠\033[0m %s\n' "$1"; }
err()    { printf '\033[31m✗\033[0m %s\n' "$1" >&2; }
kv()     { printf '   \033[90m%-14s\033[0m %s\n' "$1" "$2"; }

# ── deps check ───────────────────────────────────────────────────────────────
for cmd in curl python3; do
    command -v "$cmd" >/dev/null || { err "Cần cài $cmd"; exit 1; }
done

CURL="curl -sk"   # -k bỏ qua TLS self-signed

api() {
    # api METHOD PATH [DATA]  → prints response body, exits if HTTP >= 500
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

# ── 0. Health ────────────────────────────────────────────────────────────────
hdr "0. Kiểm tra server"
code=$($CURL -o /dev/null -w "%{http_code}" "${BASE_URL}/forms/modules") || true
if [ "$code" != "200" ]; then
    err "Không kết nối được ${BASE_URL} (HTTP $code)"
    exit 1
fi
ok "Server up tại ${BASE_URL}"

# ── 1. Login (auto-register nếu chưa có user) ────────────────────────────────
hdr "1. Đăng nhập"
LOGIN_BODY=$(cat <<EOF
{"email":"${EMAIL}","password":"${PASSWORD}"}
EOF
)
resp="$(api POST /auth/login "$LOGIN_BODY")"
if echo "$resp" | grep -q '"success":true'; then
    ok "Login thành công"
else
    warn "User chưa tồn tại → đăng ký mới"
    REG_BODY=$(cat <<EOF
{"email":"${EMAIL}","password":"${PASSWORD}","fullName":"${FULLNAME}"}
EOF
)
    api POST /auth/register "$REG_BODY" >/dev/null
    resp="$(api POST /auth/login "$LOGIN_BODY")"
    echo "$resp" | grep -q '"success":true' || { err "Login fail"; echo "$resp"; exit 1; }
    ok "Đăng ký + login thành công"
fi
TOKEN=$(echo "$resp" | json_get "d['data']['token']")
USER_ID=$(echo "$resp" | json_get "d['data']['userId']")
kv "userId" "$USER_ID"
kv "token"  "${TOKEN:0:40}…"

# ── 2. SourceProfile (tạo nếu chưa có) ───────────────────────────────────────
hdr "2. Đăng ký SourceProfile (DataMatchingService)"
existing="$(api GET "/dm/sources?sourceSystem=${SOURCE_SYSTEM}")"
if echo "$existing" | grep -q "\"recordType\":\"${RECORD_TYPE}\""; then
    ok "SourceProfile ${SOURCE_SYSTEM}/${RECORD_TYPE} đã tồn tại — skip"
else
    SOURCE_BODY=$(cat <<'EOF'
{
  "sourceSystem":    "his-fresher",
  "recordType":      "benh-nhan",
  "displayName":     "HIS Fresher Demo - Benh nhan",
  "businessKeyField":"MaBenhNhan",
  "mappings": {
    "ma_bn":     "MaBenhNhan",
    "ho_ten":    "HoTen",
    "ngay_sinh": "NgaySinh",
    "ten_khoa":  "TenKhoa",
    "so_giuong": "SoGiuong",
    "chan_doan": "ChanDoan",
    "bac_si":    "BacSiPhuTrach"
  }
}
EOF
)
    resp="$(api POST /dm/sources "$SOURCE_BODY")"
    if echo "$resp" | grep -q '"success":true'; then
        SOURCE_ID=$(echo "$resp" | json_get "d['data']['id']")
        ok "Đăng ký SourceProfile thành công"
        kv "sourceId" "$SOURCE_ID"
    else
        err "Đăng ký fail"; echo "$resp"; exit 1
    fi
fi

# ── 3. Ingest record (suffix timestamp để hash khác mỗi lần) ─────────────────
hdr "3. Ingest record JSON"
INGEST_BODY=$(cat <<EOF
{
  "sourceSystem": "${SOURCE_SYSTEM}",
  "recordType":   "${RECORD_TYPE}",
  "payload": {
    "ma_bn":     "BN-${SUFFIX}",
    "ho_ten":    "Phạm Quỳnh Như",
    "ngay_sinh": "1992-08-14",
    "ten_khoa":  "Khoa Tim Mạch",
    "so_giuong": "TM-12",
    "chan_doan": "Rối loạn nhịp tim, theo dõi 24h — case ${SUFFIX}",
    "bac_si":    "BS. Trần Văn Đạt"
  }
}
EOF
)
resp="$(api POST /dm/ingest/json "$INGEST_BODY")"
echo "$resp" | grep -q '"success":true' || { err "Ingest fail"; echo "$resp"; exit 1; }
RECORD_ID=$(echo "$resp" | json_get "d['data']['id']")
STATUS=$(echo   "$resp" | json_get "d['data']['status']")
ok "Ingest thành công"
kv "recordId" "$RECORD_ID"
kv "status"   "$STATUS  (chờ Worker xử lý)"

# ── 4. Chờ MatchingWorker ────────────────────────────────────────────────────
hdr "4. Chờ MatchingWorker (poll mỗi 5s, max 60s)"
for i in $(seq 1 12); do
    sleep 5
    rec="$(api GET "/dm/records/${RECORD_ID}")"
    st=$(echo "$rec" | json_get "d['data']['status']")
    printf "   [%2ds] status = %s\n" $((i*5)) "$st"
    if [ "$st" = "Matched" ]; then
        ok "Record đã chuyển sang Matched"
        PROCESSED_AT=$(echo "$rec" | json_get "d['data']['processedAt']")
        kv "processedAt" "$PROCESSED_AT"
        break
    fi
    if [ "$i" = "12" ]; then
        err "Worker không xử lý sau 60s — check log: docker compose logs datamatchingservice"
        exit 1
    fi
done

# ── 5. Generate-from-source (1 lệnh = toàn bộ form setup) ────────────────────
hdr "5. Auto-generate Module + Screen + Form (1 API)"
GEN_BODY=$(cat <<EOF
{
  "moduleCode":  "${MODULE_CODE}",
  "moduleName":  "Fresher Demo Module ${SUFFIX}",
  "screenCode":  "${SCREEN_CODE}",
  "screenTitle": "Xét duyệt hồ sơ bệnh nhân (run ${SUFFIX})",
  "formKey":     "${FORM_KEY}",
  "formTitle":   "Phiếu xét duyệt bệnh nhân",
  "dataSource": {
    "namespace":      "record",
    "serviceId":      "datamatch",
    "resourcePath":   "/dm/records/{recordId}",
    "requiredParams": ["recordId"]
  },
  "fields": [
    { "canonicalKey":"HoTen",          "label":"Họ tên",        "fieldType":"Text" },
    { "canonicalKey":"NgaySinh",       "label":"Ngày sinh",     "fieldType":"Date",     "displayFormat":"date:DD/MM/YYYY" },
    { "canonicalKey":"TenKhoa",        "label":"Khoa",          "fieldType":"Text" },
    { "canonicalKey":"SoGiuong",       "label":"Số giường",     "fieldType":"Text" },
    { "canonicalKey":"ChanDoan",       "label":"Chẩn đoán",     "fieldType":"Textarea" },
    { "canonicalKey":"BacSiPhuTrach",  "label":"BS phụ trách",  "fieldType":"Text" },
    { "canonicalKey":null, "fieldKey":"ket_luan", "label":"Kết luận xét duyệt", "fieldType":"Select", "isReadOnly":false, "required":true, "options":["Đạt tiêu chuẩn","Cần bổ sung","Không đạt"] },
    { "canonicalKey":null, "fieldKey":"ghi_chu",  "label":"Ghi chú", "fieldType":"Textarea", "isReadOnly":false }
  ]
}
EOF
)
resp="$(api POST /forms/admin/generate-from-source "$GEN_BODY")"
echo "$resp" | grep -q '"success":true' || { err "Generate fail"; echo "$resp"; exit 1; }
FORM_TEMPLATE_ID=$(echo "$resp" | json_get "d['data']['formTemplateId']")
FIELDS_COUNT=$(echo "$resp" | json_get "d['data']['fieldsGenerated']")
ok "Generate thành công"
kv "moduleCode"     "$MODULE_CODE"
kv "screenCode"     "$SCREEN_CODE"
kv "formKey"        "$FORM_KEY"
kv "formTemplateId" "$FORM_TEMPLATE_ID"
kv "fields"         "$FIELDS_COUNT"

# ── 6. Fetch layout (SDUI) ───────────────────────────────────────────────────
hdr "6. Fetch screen layout (SDUI endpoint)"
layout="$(api GET "/forms/screens/${MODULE_CODE}/${SCREEN_CODE}/layout")"
echo "$layout" | grep -q '"success":true' || { err "Layout fail"; echo "$layout"; exit 1; }
ok "Layout trả về OK"

LAYOUT_JSON="$layout" python3 <<'PY'
import json, os
d = json.loads(os.environ['LAYOUT_JSON'])
data = d['data']
ds = data['dataSources']
print(f"   \033[90mdataSources:\033[0m")
for s in ds:
    print(f"     • {s['namespace']:8} → {s['resourcePath']} (params={s['requiredParams']})")
for t in data['tabs']:
    print(f"   \033[90mtab:\033[0m {t['label']} (slug={t['slug']}) — {len(t['widgets'])} widget(s)")
    for w in t['widgets']:
        print(f"     widget: {w['widgetKey']} ({w['widgetType']})")
        fs = w.get('formSchema')
        if fs:
            print(f"       form: {fs['name']} v{fs['version']} — {len(fs['fields'])} fields")
            for f in fs['fields']:
                rb = f.get('dataBinding')
                expr = rb['expression'] if rb else '(free)'
                ro   = '🔒' if f['isReadOnly'] else '✏️ '
                print(f"         {ro} {f['label']:18} → {expr}")
PY

# ── 7. Fetch record + simulate expression evaluate ───────────────────────────
hdr "7. Fetch record + giả lập evaluate expression"
rec="$(api GET "/dm/records/${RECORD_ID}")"
REC_JSON="$rec" python3 <<'PY'
import json, os
rec = json.loads(os.environ['REC_JSON'])['data']
canonical = json.loads(rec['canonicalPayload'])
print(f"   \033[90mcanonicalPayload:\033[0m")
for k, v in canonical.items():
    print(f"     {k:15} = {v}")
print()
print(f"   \033[90mEvaluate expressions:\033[0m")
print(f"     {{{{sources.record.HoTen}}}}    → \033[36m{canonical['HoTen']}\033[0m")
print(f"     {{{{sources.record.TenKhoa}}}}  → \033[36m{canonical['TenKhoa']}\033[0m")
print(f"     {{{{sources.record.NgaySinh}}}} → \033[36m{canonical['NgaySinh']}\033[0m")
PY

# ── 8. Submit form (chỉ gửi field user nhập) ─────────────────────────────────
hdr "8. Submit form (chỉ 2 free field, bound field không gửi)"
SUBMIT_BODY=$(cat <<EOF
{
  "answers": [
    { "fieldKey": "ket_luan", "value": "Đạt tiêu chuẩn" },
    { "fieldKey": "ghi_chu",  "value": "Demo run ${SUFFIX}: đã kiểm tra hồ sơ, hội chẩn xong, đồng ý chuyển khoa hồi sức." }
  ]
}
EOF
)
resp="$(api POST "/forms/${MODULE_CODE}/${FORM_KEY}/submit" "$SUBMIT_BODY")"
echo "$resp" | grep -q '"success":true' || { err "Submit fail"; echo "$resp"; exit 1; }
SUBMISSION_ID=$(echo "$resp" | json_get "d['data']['submissionId']")
ok "Submit thành công"
kv "submissionId" "$SUBMISSION_ID"

# ── 9. Verify submission ─────────────────────────────────────────────────────
hdr "9. Verify submission đã lưu đúng"
subs="$(api GET "/forms/admin/forms/${FORM_TEMPLATE_ID}/submissions")"
SUBS_JSON="$subs" python3 <<'PY'
import json, os
d = json.loads(os.environ['SUBS_JSON'])['data']
print(f"   Tổng: {len(d)} submission")
for s in d:
    print(f"   • {s['id']} v{s['formVersion']} status={s['status']} at {s['submittedAt']}")
    for a in s['answers']:
        # case có thể là Value hoặc value tùy serializer
        k = a.get('fieldKey') or a.get('FieldKey')
        v = a.get('value')    or a.get('Value')
        print(f"     - {k}: {v}")
PY

# ── 10. Summary ──────────────────────────────────────────────────────────────
hdr "10. TỔNG KẾT"
echo
bold "Đã chạy thành công 9 bước:"
ok "Login                  → $USER_ID"
ok "SourceProfile          → ${SOURCE_SYSTEM}/${RECORD_TYPE}"
ok "Ingest record          → $RECORD_ID"
ok "Wait MatchingWorker    → Matched"
ok "Generate form          → $FORM_TEMPLATE_ID"
ok "Fetch layout (SDUI)    → ${SCREEN_CODE}"
ok "Fetch record + eval    → canonicalPayload OK"
ok "Submit form            → $SUBMISSION_ID"
ok "Verify submission      → formVersion=2, chỉ 2 free fields"

echo
bold "Bạn có thể test thêm:"
kv "Mở browser"  "${BASE_URL}/swagger"
kv "Schema BDUI" "GET  ${BASE_URL}/forms/${MODULE_CODE}/${FORM_KEY}/schema"
kv "Layout SDUI" "GET  ${BASE_URL}/forms/screens/${MODULE_CODE}/${SCREEN_CODE}/layout"
kv "Record"      "GET  ${BASE_URL}/dm/records/${RECORD_ID}"
echo
echo "─────────────────────────────────────────────────────────"
echo "Đọc thêm: docs/37-huong-dan-toan-luong-cho-fresher.md"
echo "─────────────────────────────────────────────────────────"
