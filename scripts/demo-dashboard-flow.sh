#!/usr/bin/env bash
# demo-dashboard-flow.sh
# ─────────────────────────────────────────────────────────────────────────────
# Script demo end-to-end tạo MỘT MÀN HÌNH DASHBOARD với widgets có DATA THẬT.
# Khác với demo-fresher-flow.sh (làm form input), script này làm DASHBOARD READ-ONLY
# với 3 widget: KpiCard + PieChart + Table.
#
# Chạy:   bash scripts/demo-dashboard-flow.sh
# Env:    BASE_URL  (default https://192.168.100.60:8443)
#         EMAIL     (default freshertest@hdos.local)
#         PASSWORD  (default Fresher@123)
#
# Idempotent: mỗi lần chạy tạo module mới với suffix timestamp.
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

BASE_URL="${BASE_URL:-https://192.168.100.60:8443}"
EMAIL="${EMAIL:-freshertest@hdos.local}"
PASSWORD="${PASSWORD:-Fresher@123}"
FULLNAME="${FULLNAME:-Fresher Tester}"

SUFFIX="$(date +%s)"
SOURCE_SYSTEM="his-fresher"
RECORD_TYPE="benh-nhan"
MODULE_CODE="hospital-dash-${SUFFIX}"
SCREEN_CODE="overview"

# ── color helpers ────────────────────────────────────────────────────────────
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

# ── 2. Ensure SourceProfile + Ingest data đa dạng (3 khoa) ───────────────────
hdr "2. Đảm bảo SourceProfile + Ingest 5 record (3 khoa)"
existing="$(api GET "/dm/sources?sourceSystem=${SOURCE_SYSTEM}")"
if ! echo "$existing" | grep -q "\"recordType\":\"${RECORD_TYPE}\""; then
    api POST /dm/sources '{
      "sourceSystem":"his-fresher","recordType":"benh-nhan",
      "displayName":"HIS Fresher Demo","businessKeyField":"MaBenhNhan",
      "mappings":{"ma_bn":"MaBenhNhan","ho_ten":"HoTen","ngay_sinh":"NgaySinh",
                  "ten_khoa":"TenKhoa","so_giuong":"SoGiuong","chan_doan":"ChanDoan","bac_si":"BacSiPhuTrach"}
    }' >/dev/null
    ok "Đã đăng ký SourceProfile"
else
    ok "SourceProfile đã có"
fi

# Ingest 5 record với 3 khoa khác nhau để Chart đa dạng
INGEST_COUNT=0
ingest_one() {
    local khoa="$1" gid="$2" diag="$3"
    local ts="$(date +%s%N)"
    local body
    body=$(cat <<EOF
{
  "sourceSystem":"${SOURCE_SYSTEM}","recordType":"${RECORD_TYPE}",
  "payload":{
    "ma_bn":"BN-${SUFFIX}-${ts}",
    "ho_ten":"Bệnh nhân ${gid}",
    "ngay_sinh":"1990-01-01",
    "ten_khoa":"${khoa}",
    "so_giuong":"${gid}",
    "chan_doan":"${diag}",
    "bac_si":"BS. Demo"
  }
}
EOF
)
    resp="$(api POST /dm/ingest/json "$body")"
    if echo "$resp" | grep -q '"success":true'; then
        INGEST_COUNT=$((INGEST_COUNT + 1))
    fi
}

ingest_one "Khoa Tim Mạch" "TM-${SUFFIX}-01" "Rối loạn nhịp tim"
ingest_one "Khoa Tim Mạch" "TM-${SUFFIX}-02" "Suy tim độ 2"
ingest_one "ICU"           "ICU-${SUFFIX}-01" "Suy hô hấp nặng"
ingest_one "ICU"           "ICU-${SUFFIX}-02" "Sốc nhiễm trùng"
ingest_one "Khoa Nhi"      "NHI-${SUFFIX}-01" "Sốt cao co giật"
ok "Đã ingest ${INGEST_COUNT}/5 record (đợi Worker xử lý)"

# Đợi Worker
printf "   "
for i in $(seq 1 12); do
    sleep 5
    pending=$(api GET "/dm/records?sourceSystem=${SOURCE_SYSTEM}&recordType=${RECORD_TYPE}&limit=50" \
        | python3 -c "import sys,json; d=json.load(sys.stdin)['data']; print(sum(1 for r in d if r['status']=='Pending'))")
    printf "[%2ds] pending=%s " $((i*5)) "$pending"
    if [ "$pending" = "0" ]; then
        echo
        ok "Tất cả record đã Matched"
        break
    fi
done

# ── 3. Tạo module + screen ───────────────────────────────────────────────────
hdr "3. Tạo Module + Screen (Draft)"
api POST /forms/admin/modules \
    "{\"code\":\"${MODULE_CODE}\",\"name\":\"Hospital Dashboard ${SUFFIX}\",\"description\":\"Demo widgets có data thật\"}" >/dev/null
ok "Module $MODULE_CODE"

api POST /forms/admin/screens \
    "{\"moduleCode\":\"${MODULE_CODE}\",\"code\":\"${SCREEN_CODE}\",\"title\":\"Tổng quan bệnh viện\",\"description\":\"Dashboard KPI + Table + Chart\",\"sortOrder\":0}" >/dev/null
ok "Screen $SCREEN_CODE (Draft)"

# ── 4. Set DataSources (2 nguồn) ─────────────────────────────────────────────
hdr "4. Khai báo 2 DataSources"
api PUT "/forms/admin/screens/${MODULE_CODE}/${SCREEN_CODE}/data-sources" '[
  {"namespace":"patients","serviceId":"datamatch",
   "resourcePath":"/dm/records?sourceSystem=his-fresher&recordType=benh-nhan&limit=50",
   "requiredParams":[]},
  {"namespace":"kpi_khoa","serviceId":"datamatch",
   "resourcePath":"/dm/reports/benh-nhan-theo-khoa?sourceSystem=his-fresher",
   "requiredParams":[]}
]' >/dev/null
ok "patients   → /dm/records?... (list)"
ok "kpi_khoa   → /dm/reports/benh-nhan-theo-khoa (aggregate report)"

# ── 5. Tạo tab ───────────────────────────────────────────────────────────────
hdr "5. Tạo tab \"Tổng quan\""
tab_resp="$(api POST "/forms/admin/screens/${MODULE_CODE}/${SCREEN_CODE}/tabs" \
    '{"label":"Tổng quan","slug":"main","sortOrder":0,"isDefault":true}')"
TAB_ID=$(echo "$tab_resp" | json_get "d['data']['id']")
ok "Tab ID = $TAB_ID"

# ── 6. Save 3 widgets ────────────────────────────────────────────────────────
hdr "6. Save 3 widget (KpiCard + PieChart + Table)"
WIDGET_PAYLOAD=$(python3 <<'PY'
import json
widgets = [
    {
        "widgetKey":"kpi-total","widgetType":"KpiCard",
        "gridX":0,"gridY":0,"gridW":8,"gridH":4,
        "configJson":json.dumps({
            "title":"Tổng số bệnh nhân",
            "valueExpression":"{{sources.kpi_khoa.summary.TotalRecords}}",
            "unit":"người","color":"#6366f1","icon":"users",
        }, ensure_ascii=False),
        "referenceId":None,
    },
    {
        "widgetKey":"chart-by-khoa","widgetType":"PieChart",
        "gridX":8,"gridY":0,"gridW":16,"gridH":8,
        "configJson":json.dumps({
            "title":"Bệnh nhân theo Khoa","chartType":"pie",
            "dataExpression":"{{sources.kpi_khoa.rows}}",
            "rowPath":"data","labelField":"TenKhoa","valueField":"SoBenhNhan",
            "colors":["#6366f1","#ec4899","#10b981","#f59e0b","#06b6d4"],
        }, ensure_ascii=False),
        "referenceId":None,
    },
    {
        "widgetKey":"table-patients","widgetType":"Table",
        "gridX":0,"gridY":4,"gridW":24,"gridH":12,
        "configJson":json.dumps({
            "title":"Danh sách bệnh nhân",
            "dataExpression":"{{sources.patients}}",
            "canonicalAtKey":"canonicalPayload",
            "columns":[
                {"field":"HoTen","header":"Họ tên","width":220},
                {"field":"TenKhoa","header":"Khoa","width":160},
                {"field":"SoGiuong","header":"Giường","width":100},
                {"field":"ChanDoan","header":"Chẩn đoán","width":280},
                {"field":"BacSiPhuTrach","header":"BS phụ trách","width":200},
            ],
            "emptyMessage":"Chưa có bệnh nhân",
        }, ensure_ascii=False),
        "referenceId":None,
    },
]
print(json.dumps(widgets, ensure_ascii=False))
PY
)
api PUT "/forms/admin/screens/${MODULE_CODE}/${SCREEN_CODE}/tabs/${TAB_ID}/widgets" "$WIDGET_PAYLOAD" >/dev/null
ok "Đã lưu 3 widget"

# ── 7. Publish ───────────────────────────────────────────────────────────────
hdr "7. Publish screen"
api POST "/forms/admin/screens/${MODULE_CODE}/${SCREEN_CODE}/publish" "" >/dev/null
ok "Screen Published"

# ── 8. Fetch layout + giả lập FE render ──────────────────────────────────────
hdr "8. Giả lập Frontend: fetch layout + fetch DataSources + render"
LAYOUT_JSON="$(api GET "/forms/screens/${MODULE_CODE}/${SCREEN_CODE}/layout")"
PATIENTS_JSON="$(api GET "/dm/records?sourceSystem=${SOURCE_SYSTEM}&recordType=${RECORD_TYPE}&limit=50")"
KPI_JSON="$(api GET "/dm/reports/benh-nhan-theo-khoa?sourceSystem=${SOURCE_SYSTEM}")"

LAYOUT_JSON="$LAYOUT_JSON" PATIENTS_JSON="$PATIENTS_JSON" KPI_JSON="$KPI_JSON" python3 <<'PY'
import json, os, re, sys

layout = json.loads(os.environ['LAYOUT_JSON'])['data']
patients = json.loads(os.environ['PATIENTS_JSON'])['data']
kpi = json.loads(os.environ['KPI_JSON'])['data']
sources = {'patients': patients, 'kpi_khoa': kpi}

def resolve(expr, sources):
    m = re.match(r'^\{\{\s*sources\.([\w.]+)\s*\}\}$', expr or '')
    if not m: return None
    cur = sources
    for p in m.group(1).split('.'):
        if cur is None: return None
        cur = cur.get(p) if isinstance(cur, dict) else None
    return cur

print()
print('═' * 72)
print(f'\033[1;36m   📊 DASHBOARD: {layout["title"]}\033[0m')
print('═' * 72)

for tab in layout['tabs']:
    for w in tab['widgets']:
        cfg = w.get('config') or {}
        t = w['widgetType']
        print()
        if t == 'KpiCard':
            val = resolve(cfg.get('valueExpression'), sources)
            print(f'  ┌─────────────────────────────────────────┐')
            print(f'  │ \033[90m{cfg["title"]:39}\033[0m │')
            print(f'  │                                         │')
            print(f'  │ \033[1;36m{str(val):20}\033[0m \033[90m{cfg["unit"]:18}\033[0m │')
            print(f'  └─────────────────────────────────────────┘')

        elif t == 'PieChart':
            rows = resolve(cfg.get('dataExpression'), sources) or []
            print(f'  \033[1m{cfg["title"]}\033[0m  ({cfg["chartType"]} chart)')
            print(f'  ' + '─' * 60)
            total = sum((r.get(cfg['rowPath'], {}) if cfg.get('rowPath') else r).get(cfg['valueField'], 0) for r in rows)
            for r in rows:
                d = r.get(cfg['rowPath'], r) if cfg.get('rowPath') else r
                lbl = d.get(cfg['labelField'])
                val = d.get(cfg['valueField'])
                pct = 100 * val / total if total else 0
                bar = '█' * int(pct / 3)
                print(f'    {lbl:18} {bar:35} \033[36m{val:3}\033[0m ({pct:.1f}%)')

        elif t == 'Table':
            rows = resolve(cfg.get('dataExpression'), sources) or []
            cols = cfg['columns']
            ck = cfg.get('canonicalAtKey')
            print(f'  \033[1m{cfg["title"]}\033[0m  ({len(rows)} rows)')
            print('  ' + ' │ '.join(f'\033[90m{c["header"]:16}\033[0m' for c in cols[:4]))
            print('  ' + '─' * 80)
            for r in rows[:5]:
                item = json.loads(r[ck]) if ck and ck in r else r
                print('  ' + ' │ '.join(f'{str(item.get(c["field"],"-"))[:16]:16}' for c in cols[:4]))
            if len(rows) > 5:
                print(f'  \033[90m... còn {len(rows)-5} row khác\033[0m')

print()
print('═' * 72)
PY

# ── 9. Tổng kết ──────────────────────────────────────────────────────────────
hdr "9. TỔNG KẾT"
echo
ok "Module          → $MODULE_CODE"
ok "Screen          → $SCREEN_CODE (Published)"
ok "DataSources     → 2 (patients + kpi_khoa)"
ok "Widgets         → 3 (KpiCard + PieChart + Table)"

echo
bold "API để FE thật fetch:"
kv "Layout SDUI"   "GET  ${BASE_URL}/forms/screens/${MODULE_CODE}/${SCREEN_CODE}/layout"
kv "DataSource 1"  "GET  ${BASE_URL}/dm/records?sourceSystem=${SOURCE_SYSTEM}&recordType=${RECORD_TYPE}&limit=50"
kv "DataSource 2"  "GET  ${BASE_URL}/dm/reports/benh-nhan-theo-khoa?sourceSystem=${SOURCE_SYSTEM}"

echo
echo "─────────────────────────────────────────────────────────"
echo "Đọc thêm: docs/37-huong-dan-toan-luong-cho-fresher.md (§7)"
echo "─────────────────────────────────────────────────────────"
