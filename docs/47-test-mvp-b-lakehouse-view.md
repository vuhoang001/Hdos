# 47 — Test Guide: Luồng MappingProfile từ Lakehouse View (MVP B)

> **Mục đích.** Hướng dẫn TEST end-to-end luồng auto-enroll SourceProfile từ warehouse view qua endpoint `POST /lakehouse/view-bindings/with-auto-profile` (MVP B). Đối tượng đọc: dev / QA / admin cần verify pipeline hoạt động trước khi onboard nguồn data thật vào production.
>
> **Khác với doc 46 (playbook 2 cách).** Doc 46 trình bày 2 cách (DataMatching push + Lakehouse view) ở mức tổng quan. Doc này CHỈ tập trung MVP B (Lakehouse view), đi sâu vào **TEST + DEBUG** với case study cụ thể `lakehouse_serving` (schema `api`).

---

## Mục lục

1. [Tổng quan luồng](#1-tổng-quan-luồng)
2. [Prerequisites](#2-prerequisites)
3. [Bước 0 — Inspect view schema](#3-bước-0--inspect-view-schema)
4. [Bước 1 — Tạo ViewBinding + auto MappingProfile](#4-bước-1--tạo-viewbinding--auto-mappingprofile)
5. [Bước 2 — Trigger sync](#5-bước-2--trigger-sync)
6. [Bước 3 — Verify canonical record](#6-bước-3--verify-canonical-record)
7. [Bước 4 (optional) — Render trên FE](#7-bước-4-optional--render-trên-fe)
8. [Case study: api.bed_occupancy](#8-case-study-apibed_occupancy)
9. [Case study mở rộng — 5 view còn lại trong schema api](#9-case-study-mở-rộng--5-view-còn-lại-trong-schema-api)
10. [Pitfalls — lỗi hay gặp + fix](#10-pitfalls--lỗi-hay-gặp--fix)
11. [Khi nào KHÔNG nên dùng MVP B](#11-khi-nào-không-nên-dùng-mvp-b)
12. [Liên quan](#12-liên-quan)

---

## 1. Tổng quan luồng

MVP B = "1 call gộp" — admin chỉ cần khai tên view + business-key + updated-at column. Backend tự làm phần còn lại.

```
                    POST /lakehouse/view-bindings/with-auto-profile
admin/curl  ───────────────────────────────────────────────────────►  LakehouseService
                                                                         │
                                                                         │ [1] Introspect view schema
                                                                         │     (information_schema.columns)
                                                                         ▼
                                                                    Warehouse PG
                                                                         │
                                                                         │ trả về list columns
                                                                         ▼
                                                                    LakehouseService
                                                                         │
                                                                         │ [2] Sinh mappings:
                                                                         │     snake_case → PascalCase
                                                                         │     + override healthcare
                                                                         │     (CanonicalNameSuggester)
                                                                         │
                                                                         │ [3] HTTP POST /dm/sources
                                                                         ▼
                                                                  DataMatchingService
                                                                         │ Tạo SourceProfile
                                                                         │ (idempotent: 409 = đã có)
                                                                         ▼
                                                                    LakehouseService
                                                                         │ [4] Lưu ViewBinding
                                                                         ▼
                                                                       201 Created
                                                                       (binding + mappings)
```

Sau khi binding tạo xong, **trigger sync** sẽ:

```
POST /lakehouse/view-bindings/{id}/sync
           │
           ▼
WarehouseViewSyncer:
  SELECT * FROM <view>
  Mỗi row → PublishAsync(RawRecordIngestRequestedIntegrationEvent)
           │
           ▼
        RabbitMQ
           │
           ▼
DataMatchingService consumer:
  Apply SourceProfile mappings → canonical payload
  SHA-256 dedup
  Lưu StagingRecord
           │
           ▼  (sau ~30s MatchingWorker chạy)
  Status → Matched
           │
           ▼
GET /dm/records → canonical đã sẵn cho FE
```

3 service liên kết qua **2 thứ duy nhất**:
- `(sourceSystem, recordType)` — định danh nguồn
- `recordId` — định danh 1 bản ghi canonical

---

## 2. Prerequisites

### 2.1 Stack đang chạy

```bash
cd /home/hoanggggf/Documents/Code/work/Hdos
docker compose up -d

# Verify
docker compose ps | grep -E "lakehouseservice|datamatchingservice|rabbitmq"
```

Phải thấy 3 container đều `Up`. Nếu service trả `Restarting` → `docker compose logs <service>` xem lý do (thường là sai connection string).

### 2.2 Lakehouse reachable

LakehouseService kết nối warehouse qua biến môi trường:

```bash
# Trong docker-compose.yml — services.lakehouseservice.environment:
ConnectionStrings__Warehouse: "${WAREHOUSE_CONN:-}"
```

`WAREHOUSE_CONN` lấy từ `.env`:

```bash
# Format Npgsql:
WAREHOUSE_CONN=Host=192.168.100.66;Port=5432;Database=lakehouse_serving;Username=lh_serving;Password=<secret>
```

**Verify từ máy host:**
```bash
PGPASSWORD=<secret> psql -h 192.168.100.66 -p 5432 -U lh_serving -d lakehouse_serving -c "SELECT 1;"
```
Trả về `1` → host của bạn tới được warehouse. Container `lakehouseservice` có thể tới hay không phụ thuộc network/firewall — nếu lỗi `Connection refused` lúc gọi API thì test thêm từ trong container:

```bash
docker compose exec lakehouseservice sh -c "apk add postgresql-client 2>/dev/null; \
  PGPASSWORD=<secret> psql -h 192.168.100.66 -p 5432 -U lh_serving -d lakehouse_serving -c 'SELECT 1;'"
```

### 2.3 BASE URL

```bash
# Local dev (qua nginx mặc định)
BASE=http://localhost:5000

# Hoặc trực tiếp container (bypass nginx)
BASE=http://localhost:8080      # nếu lakehouseservice expose 8080 ra host
```

Mọi curl trong doc đều dùng `$BASE` — set đúng trước khi chạy.

### 2.4 (Optional) JWT token

Nếu service yêu cầu auth, lấy token từ AuthService:

```bash
TOKEN=$(curl -s -X POST "$BASE/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@hdos.local","password":"Admin@123"}' \
  | jq -r '.data.accessToken')

# Sau đó thêm vào mọi curl:
curl -H "Authorization: Bearer $TOKEN" ...
```

ViewBindings controller hiện tại **không gắn `[Authorize]` ở method level** (xem `ViewBindingsController.cs`) — tuỳ middleware global mà có thể vẫn require JWT. Test thử không token trước, nếu 401 thì thêm.

---

## 3. Bước 0 — Inspect view schema

Phải làm trước khi gọi `with-auto-profile`. Cần biết:
- Cột nào dùng làm **`businessKeyColumn`** (định danh 1 row, có thể coi như PK của record)
- Cột nào dùng làm **`updatedAtColumn`** (timestamp/date column — validator yêu cầu)

### 3.1 List view trong schema

```bash
PGPASSWORD=<secret> psql -h 192.168.100.66 -p 5432 -U lh_serving -d lakehouse_serving -c "\dv api.*"
```

### 3.2 Inspect 1 view cụ thể

```bash
PGPASSWORD=<secret> psql -h 192.168.100.66 -p 5432 -U lh_serving -d lakehouse_serving -c "\d+ api.bed_occupancy"
```

Hoặc query SQL:

```bash
PGPASSWORD=<secret> psql -h 192.168.100.66 -p 5432 -U lh_serving -d lakehouse_serving -c "
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema='api' AND table_name='bed_occupancy'
ORDER BY ordinal_position;
"
```

### 3.3 Tiêu chí chọn cột (theo `CanonicalNameSuggester`)

| Cột | Tiêu chí | Reference |
|---|---|---|
| `businessKeyColumn` | NOT NULL, tên là `business_key`, `patient_id`, `ma_benh_nhan`, `ma_bn`, hoặc kết thúc bằng `_id` / `_key` | `IsBusinessKeyCandidate(...)` |
| `updatedAtColumn` | NOT NULL, kiểu `timestamp*` hoặc `date`, tên kết thúc `_at` hoặc `_time` | `IsUpdatedAtCandidate(...)` |

**Lưu ý quan trọng — businessKey không unique:** Nếu cột bạn chọn (vd `department_id`) lặp lại qua nhiều record (vì view có nhiều ngày), DataMatching sẽ coi mỗi record sau là **update** của record trước. Semantic trở thành "snapshot mới nhất của mỗi business-key thắng". Với view dạng aggregated daily, đây thường là điều bạn muốn cho dashboard.

Nếu cần giữ TỪNG snapshot là 1 record riêng → phải thêm cột tổng hợp (vd `id` = `date::text || '-' || department_id::text`) vào view. Không có cách bypass ở API hiện tại — validator chỉ chấp nhận 1 column name.

### 3.4 Khi view không có cột `updated_at`

Validator yêu cầu cột tồn tại nhưng **`WarehouseViewSyncer` không thực sự dùng** (xem `WarehouseViewSyncer.cs:66` — SQL là `SELECT * FROM <view>`, không có `WHERE updated_at > ...`). Nên dùng cột timestamp/date BẤT KỲ tồn tại trong view, vd `date`, `created_at`, `measured_at`.

→ **Đây là "trick" để qua validator khi view không có cột chuẩn `updated_at`.** Khi pipeline được nâng cấp lên incremental sync (doc 43 đề cập), nên thêm cột `updated_at TIMESTAMPTZ` thực sự vào view.

---

## 4. Bước 1 — Tạo ViewBinding + auto MappingProfile

### 4.1 Endpoint

```
POST {BASE}/lakehouse/view-bindings/with-auto-profile
Content-Type: application/json
```

### 4.2 Payload

```jsonc
{
  "viewName":            "api.bed_occupancy",       // Bắt buộc dạng schema.name
  "sourceSystem":        "lakehouse:bed_occupancy", // Convention: lakehouse:<view_short>
  "recordType":          "bed-occupancy",           // kebab-case
  "businessKeyColumn":   "department_id",           // chọn ở Bước 0
  "updatedAtColumn":     "date",                    // chọn ở Bước 0
  "pollIntervalSeconds": 300,                       // >= 30
  "displayName":         "Bed Occupancy (Lakehouse)"// Cho UI/log
}
```

### 4.3 Response 201 — đây chính là MappingProfile auto-sinh

```json
{
  "data": {
    "binding": {
      "id":                  "<BINDING_UUID>",
      "viewName":            "api.bed_occupancy",
      "sourceSystem":        "lakehouse:bed_occupancy",
      "recordType":          "bed-occupancy",
      "businessKeyColumn":   "department_id",
      "updatedAtColumn":     "date",
      "pollIntervalSeconds": 300,
      "isActive":            true,
      "createdAtUtc":        "2026-06-07T...",
      "updatedAtUtc":        "2026-06-07T..."
    },
    "profileEnrolled":  true,
    "businessKeyField": "DepartmentId",
    "mappings": {
      "date":                 "Date",
      "department_id":        "DepartmentId",
      "department_code":      "DepartmentCode",
      "department_name":      "KhoaDieuTri",
      "planned_bed_count":    "PlannedBedCount",
      "actual_bed_count":     "ActualBedCount",
      "configured_bed_count": "ConfiguredBedCount",
      "disabled_bed_count":   "DisabledBedCount",
      "available_bed_count":  "AvailableBedCount",
      "occupied_bed_count":   "OccupiedBedCount",
      "occupancy_ratio":      "OccupancyRatio"
    }
  }
}
```

→ **LƯU `binding.id`** cho Bước 2.

### 4.4 Hiểu phép mapping

`CanonicalNameSuggester.Suggest()` áp 3 quy tắc theo thứ tự:

1. **Healthcare domain override** — vài tên raw được map sang canonical Vietnamese chuẩn:
   - `business_key`, `patient_id`, `ma_benh_nhan`, `ma_bn` → `MaBenhNhan`
   - `department_name`, `department`, `khoa`, `khoa_dieu_tri` → `KhoaDieuTri` ⚠️
   - `patient_name`, `ho_ten`, `full_name` → `TenBenhNhan`
   - `diagnosis`, `chan_doan`, `icd10` → `ChanDoan`
   - `date_of_birth`, `ngay_sinh`, `dob` → `NgaySinh`
   - `admission_date`, `ngay_nhap_vien`, `admit_date`, `encounter_date` → `NgayNhapVien`

2. **Audit timestamps** — prefix `_` để FE bỏ qua khi render form:
   - `created_at` → `_created_at`
   - `updated_at` → `_updated_at`
   - `received_at` → `_received_at`
   - `processed_at` → `_processed_at`

3. **Fallback** — snake_case → PascalCase (`actual_bed_count` → `ActualBedCount`).

⚠️ **Cẩn thận `department_name` → `KhoaDieuTri`.** Khi gọi `generate-from-source` ở Bước 4, `canonicalKey` phải gõ chính xác `KhoaDieuTri`, không phải `DepartmentName`.

### 4.5 Idempotent

Endpoint chạy lại với cùng `(sourceSystem, recordType)` → trả 201 lần đầu, các lần sau:
- Nếu `viewName` đã có binding → **409 Conflict** (binding-level)
- Nếu binding chưa có nhưng SourceProfile đã có ở DataMatching (`(sourceSystem, recordType)` khớp) → backend silently treat như "enroll OK" qua HTTP 409 idempotent. Xem `SourceProfileEnrollClient.cs`.

Muốn test lại từ đầu:
```bash
curl -X DELETE "$BASE/lakehouse/view-bindings/<BINDING_UUID>"
# SourceProfile ở DataMatching không tự xoá — phải gọi /dm/sources/{id} DELETE nếu muốn xoá hẳn
```

---

## 5. Bước 2 — Trigger sync

### 5.1 Endpoint

```bash
BINDING_ID="<paste_uuid_từ_bước_1>"

curl -X POST "$BASE/lakehouse/view-bindings/$BINDING_ID/sync"
```

### 5.2 Response 202

```json
{
  "data": {
    "bindingId": "...",
    "viewName":  "api.bed_occupancy",
    "rowCount":  42,
    "jobId":     "sync-bed-occupancy-20260607143052",
    "duration":  "00:00:00.183",
    "errorMessage": null
  }
}
```

`rowCount` = số row trong view (mỗi row publish 1 event lên RabbitMQ).

### 5.3 Verify message đã chảy qua RabbitMQ

Mở RabbitMQ UI: `http://<server>:15672` (mặc định `guest/guest`).

Vào tab **Queues** → tìm:
```
data-matching-service:raw-record-ingest-requested-integration-event
```

Số `Messages` tăng = LakehouseService đã publish thành công. Nếu queue chưa tồn tại → consumer chưa start, check log:
```bash
docker compose logs datamatchingservice --tail 50 | grep -i "raw-record-ingest"
```

### 5.4 Trigger sync toàn bộ binding active

```bash
curl -X POST "$BASE/lakehouse/view-bindings/sync-all"
```

Lỗi 1 binding không stop các binding khác — kết quả trả về list, binding lỗi có `errorMessage` khác `null`.

### 5.5 Xem trạng thái sync gần nhất

```bash
curl "$BASE/lakehouse/view-bindings/sync-status"
```

---

## 6. Bước 3 — Verify canonical record

### 6.1 Đợi MatchingWorker

Consumer xử lý event ngay (~1-2s/record). Worker dedup + canonicalize chạy nền — đợi ~30s.

### 6.2 List records

```bash
curl "$BASE/dm/records?sourceSystem=lakehouse:bed_occupancy&recordType=bed-occupancy&limit=5"
```

Expected:

```json
{
  "data": [
    {
      "id":            "<recordId>",
      "sourceSystem":  "lakehouse:bed_occupancy",
      "recordType":    "bed-occupancy",
      "businessKey":   "18",
      "status":        "Matched",
      "canonicalPayload": "{\"Date\":\"2026-06-05\",\"DepartmentId\":18,\"DepartmentCode\":\"K26\",\"KhoaDieuTri\":\"Gây mê hồi sức\",\"PlannedBedCount\":12,\"ActualBedCount\":14,...}",
      "createdAtUtc":  "...",
      "matchedAtUtc":  "..."
    },
    ...
  ]
}
```

### 6.3 Lấy 1 record cụ thể

```bash
curl "$BASE/dm/records/<recordId>"
```

### 6.4 Nếu status vẫn `Pending`

- Đợi thêm 30s
- `docker compose logs datamatchingservice --tail 100 | grep -i matchingworker`
- Check queue chưa pile up: RabbitMQ UI → queue `data-matching-service:...` → `Messages Ready` phải về 0

---

## 7. Bước 4 (optional) — Render trên FE

Nếu muốn xem record hiển thị trong UI DynamicForm:

### 7.1 Auto-gen Screen + Form + Fields

```bash
curl -X POST "$BASE/forms/admin/generate-from-source" \
  -H 'Content-Type: application/json' \
  -d '{
    "moduleCode":  "bedmgmt",
    "moduleName":  "Quản lý giường",
    "screenCode":  "bed-occupancy-detail",
    "screenTitle": "Tỉ lệ sử dụng giường",
    "formKey":     "bed-occupancy-form",
    "formTitle":   "Chi tiết",
    "dataSource": {
      "namespace":      "record",
      "serviceId":      "datamatch",
      "resourcePath":   "/dm/records/{recordId}",
      "requiredParams": ["recordId"]
    },
    "fields": [
      { "canonicalKey": "Date",              "label": "Ngày",            "fieldType": "Date",
        "displayFormat": "date:DD/MM/YYYY" },
      { "canonicalKey": "DepartmentCode",    "label": "Mã khoa",         "fieldType": "Text" },
      { "canonicalKey": "KhoaDieuTri",       "label": "Tên khoa",        "fieldType": "Text" },
      { "canonicalKey": "PlannedBedCount",   "label": "Giường kế hoạch", "fieldType": "Number" },
      { "canonicalKey": "ActualBedCount",    "label": "Giường thực tế",  "fieldType": "Number" },
      { "canonicalKey": "OccupiedBedCount",  "label": "Đã chiếm",        "fieldType": "Number" },
      { "canonicalKey": "AvailableBedCount", "label": "Còn trống",       "fieldType": "Number" },
      { "canonicalKey": "OccupancyRatio",    "label": "Tỉ lệ",           "fieldType": "Number" }
    ]
  }'
```

⚠️ **Lưu ý `KhoaDieuTri`** — đây là canonical name auto-sinh từ override (`department_name` → `KhoaDieuTri`), không phải `DepartmentName`.

### 7.2 Mở FE

```
<FE_URL>/screen?module=bedmgmt&page=bed-occupancy-detail&recordId=<recordId>
```

FE tự:
1. `GET /forms/screens/bedmgmt/bed-occupancy-detail/layout` → biết widget + DataSource
2. `GET /dm/records/<recordId>` → lấy canonical payload
3. Render `FormSectionWidget` pre-fill data

---

## 8. Case study: api.bed_occupancy

### 8.1 Schema view

```
Column              | Type             | Nullable
--------------------+------------------+---------
date                | date             | NO
department_id       | integer          | NO
department_code     | text             | YES
department_name     | text             | YES
planned_bed_count   | integer          | YES
actual_bed_count    | integer          | YES
configured_bed_count| integer          | YES
disabled_bed_count  | integer          | YES
available_bed_count | integer          | YES
occupied_bed_count  | integer          | YES
occupancy_ratio     | numeric          | YES
```

### 8.2 Quyết định

| Decision | Giá trị | Lý do |
|---|---|---|
| `businessKeyColumn` | `department_id` | Cột NOT NULL, kết thúc `_id` → candidate. Semantic: 1 record / khoa, snapshot mới nhất thắng. |
| `updatedAtColumn` | `date` | View không có `updated_at`. `date` kiểu DATE thoả validator. Syncer không thực sự dùng cột này nên OK. |
| `sourceSystem` | `lakehouse:bed_occupancy` | Convention rõ ràng nguồn lakehouse + tên view |
| `recordType` | `bed-occupancy` | kebab-case |

### 8.3 Payload đầy đủ

```bash
BASE=http://localhost:5000

curl -X POST "$BASE/lakehouse/view-bindings/with-auto-profile" \
  -H 'Content-Type: application/json' \
  -d '{
    "viewName":            "api.bed_occupancy",
    "sourceSystem":        "lakehouse:bed_occupancy",
    "recordType":          "bed-occupancy",
    "businessKeyColumn":   "department_id",
    "updatedAtColumn":     "date",
    "pollIntervalSeconds": 300,
    "displayName":         "Bed Occupancy (Lakehouse api)"
  }'
```

### 8.4 Expected mappings

| Raw column | Canonical (auto) | Quy tắc áp |
|---|---|---|
| `date` | `Date` | Fallback PascalCase |
| `department_id` | `DepartmentId` | Fallback PascalCase |
| `department_code` | `DepartmentCode` | Fallback PascalCase |
| `department_name` | **`KhoaDieuTri`** | Override healthcare ⚠️ |
| `planned_bed_count` | `PlannedBedCount` | Fallback PascalCase |
| `actual_bed_count` | `ActualBedCount` | Fallback PascalCase |
| `configured_bed_count` | `ConfiguredBedCount` | Fallback PascalCase |
| `disabled_bed_count` | `DisabledBedCount` | Fallback PascalCase |
| `available_bed_count` | `AvailableBedCount` | Fallback PascalCase |
| `occupied_bed_count` | `OccupiedBedCount` | Fallback PascalCase |
| `occupancy_ratio` | `OccupancyRatio` | Fallback PascalCase |

`businessKeyField` = `DepartmentId` (canonical name của `department_id`).

---

## 9. Case study mở rộng — 5 view còn lại trong schema api

Đây là template — mỗi view bạn cần inspect rồi điền cột chính xác trước khi gọi API.

### Lệnh inspect chung

```bash
for v in clinical_pathway encounter_activity_daily finance_daily inpatient_summary_daily medicine_stock; do
  echo "===== api.$v ====="
  PGPASSWORD=<secret> psql -h 192.168.100.66 -p 5432 -U lh_serving -d lakehouse_serving -c "
    SELECT column_name, data_type, is_nullable
    FROM information_schema.columns
    WHERE table_schema='api' AND table_name='$v'
    ORDER BY ordinal_position;"
done
```

### 9.1 Gợi ý chọn cột

Áp `IsBusinessKeyCandidate` + `IsUpdatedAtCandidate`:

| View | businessKey candidate (gợi ý) | updatedAt candidate (gợi ý) | sourceSystem | recordType |
|---|---|---|---|---|
| `api.clinical_pathway` | `patient_id` (nếu có) hoặc `pathway_id` | `created_at` / `updated_at` / `encounter_date` | `lakehouse:clinical_pathway` | `clinical-pathway` |
| `api.encounter_activity_daily` | `encounter_id` hoặc `activity_id` | `date` / `activity_date` | `lakehouse:encounter_activity_daily` | `encounter-activity-daily` |
| `api.finance_daily` | `department_id` hoặc `account_id` | `date` | `lakehouse:finance_daily` | `finance-daily` |
| `api.inpatient_summary_daily` | `patient_id` hoặc `department_id` | `date` | `lakehouse:inpatient_summary_daily` | `inpatient-summary-daily` |
| `api.medicine_stock` | `medicine_id` hoặc `sku` | `updated_at` / `as_of_date` | `lakehouse:medicine_stock` | `medicine-stock` |

> Cột thực tế phụ thuộc DE — paste output `\d+ api.<view>` vào doc nội bộ rồi quyết định.

### 9.2 Template payload

```bash
curl -X POST "$BASE/lakehouse/view-bindings/with-auto-profile" \
  -H 'Content-Type: application/json' \
  -d '{
    "viewName":            "api.<VIEW_NAME>",
    "sourceSystem":        "lakehouse:<VIEW_NAME>",
    "recordType":          "<kebab-case>",
    "businessKeyColumn":   "<CHỌN_TỪ_INSPECT>",
    "updatedAtColumn":     "<CHỌN_TỪ_INSPECT>",
    "pollIntervalSeconds": 300,
    "displayName":         "<Human-readable name>"
  }'
```

### 9.3 Pitfall các view với mapping override

Vài override trong `CanonicalNameSuggester` có thể "đè" lên tên bạn không ngờ tới:

| Raw column | Canonical | Áp cho view nào sẽ "bất ngờ" |
|---|---|---|
| `department_name` | `KhoaDieuTri` | `bed_occupancy`, `finance_daily`, `inpatient_summary_daily` (nếu có cột này) |
| `diagnosis` / `diagnosis_name` | `ChanDoan` | `clinical_pathway` (nếu có) |
| `patient_name` / `ho_ten` / `full_name` | `TenBenhNhan` | `inpatient_summary_daily` (nếu có) |
| `business_key` / `patient_id` / `ma_bn` | `MaBenhNhan` | `clinical_pathway`, `inpatient_summary_daily` |

Sau khi gọi `with-auto-profile`, **luôn check response `mappings`** để biết tên canonical chính xác. Đừng đoán.

---

## 10. Pitfalls — lỗi hay gặp + fix

| Triệu chứng | Nguyên nhân | Fix |
|---|---|---|
| 400 `"Cột '<x>' không có trong view '<v>'"` | Sai tên cột businessKey/updatedAt | Quay lại Bước 0 verify chính xác qua `\d+ <view>` |
| 400 `"ViewName phải có dạng 'schema.view_name'"` | Quên schema (vd gõ `bed_occupancy` thay vì `api.bed_occupancy`) | Thêm schema |
| 400 `"PollIntervalSeconds must be >= 30"` | Truyền < 30 | Min là 30. Recommend 300 (5 phút) |
| 404 `"View '<v>' hoặc hdos_reader thiếu quyền SELECT"` | View không tồn tại / user warehouse không có quyền | Verify view + `GRANT SELECT ON api.<v> TO lh_serving;` ở warehouse PG |
| 409 Conflict | Đã có binding cho cùng `viewName` | `DELETE /lakehouse/view-bindings/<id>` trước, hoặc `PUT /lakehouse/view-bindings/<id>` nếu chỉ update |
| 502 Bad Gateway | DataMatching unreachable từ Lakehouse | Check env `Services__DataMatching__BaseUrl` của container `lakehouseservice`. Restart: `docker compose restart lakehouseservice` |
| Sync 200 nhưng `rowCount: 0` | View thực sự không có row | Verify: `SELECT COUNT(*) FROM api.<v>;` từ psql |
| RabbitMQ queue chưa có | Consumer chưa start | `docker compose logs datamatchingservice` xem có log MassTransit consumer registration |
| `/dm/records` rỗng sau sync | Event chưa được consume / dedup skip / SourceProfile mismatch | 1. Queue `Messages Ready` > 0 = chưa consume. 2. Check `docker compose logs datamatchingservice` filter `RawRecordIngestRequestedConsumer` |
| Record có `status: "Pending"` mãi | MatchingWorker chưa chạy / lỗi | `docker compose logs datamatchingservice | grep MatchingWorker` |
| FE render trắng | Quên auto-gen form ở Bước 4 | Chạy `POST /forms/admin/generate-from-source` |
| FE pre-fill rỗng dù record có data | `canonicalKey` ở form sai case | Tên canonical case-sensitive. Vd phải `KhoaDieuTri`, không phải `khoaDieuTri` hay `DepartmentName` |
| Trùng record sau sync nhiều lần | SHA-256 dedup ON payload — payload identical bị skip | Bình thường — idempotent. Đổi 1 field bất kỳ trong source row thì record mới |
| Connection refused lúc gọi API | Container `lakehouseservice` không tới được `192.168.100.66:5432` | Verify từ trong container (xem §2.2). Có thể docker network không thấy IP host — set `network_mode: host` cho lakehouseservice nếu cần test nhanh |

### Debug commands cheat sheet

```bash
# Log tail
docker compose logs lakehouseservice  --tail 100 -f
docker compose logs datamatchingservice --tail 100 -f
docker compose logs rabbitmq --tail 50

# DB peek (lakehouse-local DB của LakehouseService, không phải warehouse)
docker compose exec sqlserver /opt/mssql-tools/bin/sqlcmd \
  -S localhost -U sa -P <SA_PWD> -d LakehouseDb \
  -Q "SELECT TOP 10 * FROM ViewBindings ORDER BY CreatedAtUtc DESC"

# DataMatching DB peek (Postgres)
docker compose exec postgres-dm psql -U postgres -d DataMatchingDb \
  -c "SELECT id, source_system, record_type, business_key, status FROM raw_records ORDER BY created_at_utc DESC LIMIT 10;"

# Restart 1 service
docker compose restart lakehouseservice
```

---

## 11. Khi nào KHÔNG nên dùng MVP B

MVP B đi qua DataMatching → có overhead 1 hop (RabbitMQ + consumer + dedup). Không phù hợp khi:

| Scenario | Lý do | Hướng thay thế |
|---|---|---|
| Dashboard analytics realtime, refresh < 30s | Lag do queue + worker | Direct view query (cần thêm DataSource type — chưa implement) |
| Aggregated data không có business key thực sự (vd KPI tổng) | businessKey "ép" sẽ bóp méo semantic | Direct view query hoặc tạo materialized view với synthetic key |
| Read-only chart không cần record-level matching | Mất công canonicalize cho data không match nhau | Direct view query |
| View thay đổi schema thường xuyên | Phải re-create binding mỗi lần | Direct view query với schema detection runtime |

→ Khi cần các trường hợp trên, mở doc thiết kế **"Direct Lakehouse View DataSource"** (chưa có — sẽ là doc 48 nếu implement).

### Quy tắc thực dụng

> **Record nghiệp vụ (patient, encounter, lab) → MVP B (qua DataMatching).**
> **Analytics / dashboard / KPI → Direct view (khi đã implement).**

---

## 12. Liên quan

- [22 — CDC với Debezium + Kafka](./22-cdc-debezium-kafka.md) — alternative realtime < 5s
- [23 — DataMatchingService](./23-data-matching-service.md) — core canonicalize engine
- [29 — DynamicFormService](./29-dynamic-form-service.md) — DataSource + screen
- [36 — DataMatch → DynForm Flow](./36-datamatch-to-dynform-flow.md) — chi tiết auto-gen form
- [39 — Lakehouse Service](./39-lakehouse-service.md) — kiến trúc tổng quan
- [40 — Schema Discovery](./40-schema-discovery.md) — preview schema (hướng C)
- [43 — Warehouse Sync to Lakehouse](./43-warehouse-sync-to-lakehouse.md) — pattern poll view
- [44 — Unified Ingest Pipeline](./44-unified-ingest-pipeline.md) — kiến trúc Phase 2
- [45 — Lakehouse Auto-Enroll SourceProfile](./45-lakehouse-auto-sourceprofile.md) — implementation MVP B + hướng C
- [46 — Playbook Thêm Source Data](./46-playbook-add-source-data.md) — playbook 2 cách tổng quan
