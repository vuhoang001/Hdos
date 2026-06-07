# 45 — Lakehouse Auto-Enroll SourceProfile

> **TL;DR.** Khi admin tạo `ViewBinding` ở LakehouseService, thay vì bắt admin gõ tay `POST /dm/sources` trước, **LakehouseService tự introspect schema view → suggest mapping → enroll SourceProfile sang DataMatching** trong cùng 1 transaction logic. Admin chỉ click 1 lần.
>
> Tài liệu này mở rộng [doc 44](./44-unified-ingest-pipeline.md) — giải quyết friction lớn nhất khi onboard 1 lakehouse view mới.

**Áp dụng cho:** Phase 2 đã setup (xem doc 44). Phần này là Phase 2.5 — automation tăng productivity admin.

**Hai phần:**
- [Phần 1](#phần-1--xác-nhận-2-nguồn-data-đã-thống-nhất) — Recap: data từ 2 nguồn đã đi cùng pipeline (không cần code thêm, chỉ là verify hiểu đúng)
- [Phần 2](#phần-2--auto-enroll-sourceprofile-khi-tạo-viewbinding) — Auto-enroll SourceProfile (core feature của doc này)

---

## Mục lục

1. [Phần 1 — Xác nhận 2 nguồn data đã thống nhất](#phần-1--xác-nhận-2-nguồn-data-đã-thống-nhất)
2. [Walkthrough end-to-end — 2 case sử dụng](#2-walkthrough-end-to-end--2-case-sử-dụng)
3. [Phần 2 — Auto-enroll SourceProfile khi tạo ViewBinding](#phần-2--auto-enroll-sourceprofile-khi-tạo-viewbinding)
4. [Vấn đề friction hiện tại](#4-vấn-đề-friction-hiện-tại)
5. [Ba hướng giải quyết — so sánh](#5-ba-hướng-giải-quyết--so-sánh)
6. [Hướng C — Preview + Compound Create (recommend)](#6-hướng-c--preview--compound-create-recommend)
7. [Hướng B — Auto-convention (MVP nhanh)](#7-hướng-b--auto-convention-mvp-nhanh)
8. [Cấu hình inter-service](#8-cấu-hình-inter-service)
9. [Edge cases + quyết định kỹ thuật](#9-edge-cases--quyết-định-kỹ-thuật)
10. [Implementation order](#10-implementation-order)
11. [Testing checklist](#11-testing-checklist)

---

## Phần 1 — Xác nhận 2 nguồn data đã thống nhất

> Đọc trước phần này để chắc nền tảng. Đây không phải feature mới — chỉ verify Phase 2 (doc 44) đã làm xong điều bạn nghĩ.

### 1.1 Sau Phase 2, 2 nguồn đi đâu?

```
┌──────────────────────┐                    ┌──────────────────────┐
│ HIS push REST        │                    │ Lakehouse PG view    │
│ POST /dm/ingest/json │                    │ WarehouseViewSyncer  │
└──────────┬───────────┘                    └──────────┬───────────┘
           │                                           │ publish event
           │                                           ▼
           │                                  ┌──────────────────┐
           │                                  │ RabbitMQ         │
           │                                  │ RawRecord...     │
           │                                  └──────────┬───────┘
           │                                             │ consume
           ▼                                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ IIngestCoreService.TryBuildRecordAsync (1 codepath duy nhất)    │
│   ├─ Lookup SourceProfile by (SourceSystem, RecordType)         │
│   ├─ Apply field mapping → canonical payload                    │
│   ├─ SHA-256 dedup                                              │
│   └─ Save StagingRecord                                         │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
          ┌────────────────────────────┐
          │ staging_records (1 table)  │
          │  id │ sourceSystem │ ...   │
          │  a-1│ his-01       │       │
          │  b-1│ bhyt-hn      │       │
          │  c-1│ lakehouse:.. │       │
          └─────────────┬──────────────┘
                        │
                        ▼
        GET /dm/records[/{id}][?sourceSystem=X&recordType=Y]
                        │
                        ▼
                  FE / DynForm DataSource
```

### 1.2 Endpoints để truy xuất

Tất cả qua `GET /dm/records*`:

| Use case | Query |
|---|---|
| 1 record cụ thể | `GET /dm/records/{id}` |
| Tất cả từ 1 nguồn | `GET /dm/records?sourceSystem=his-01` |
| Cùng record type, mọi nguồn | `GET /dm/records?recordType=benh-nhan` |
| 1 nguồn + 1 type | `GET /dm/records?sourceSystem=lakehouse:v_lab_results_v1&recordType=lab-result` |
| Filter theo businessKey | `GET /dm/records?businessKey=BN-2024-001` (tất cả version/source của 1 BN) |
| Tất cả | `GET /dm/records` |

→ **FE không cần biết source — chỉ cần biết cặp `(sourceSystem, recordType)` hoặc `recordId`.** Source-agnostic.

### 1.3 JOIN 2 nguồn cho 1 màn DynForm

Khi cần hiển thị "bệnh nhân từ HIS + xét nghiệm từ Lakehouse" trong cùng form:

**Không cần endpoint join ở BE.** DynamicForm screen khai 2 DataSource:

```json
{
  "dataSources": [
    {
      "namespace":      "patient",
      "resourcePath":   "/dm/records/{recordId}",
      "requiredParams": ["recordId"]
    },
    {
      "namespace":      "labs",
      "resourcePath":   "/dm/records?sourceSystem=lakehouse:v_lab_results_v1&recordType=lab-result&businessKey={maBN}",
      "requiredParams": ["maBN"]
    }
  ]
}
```

FE `useDataSources` fetch song song 2 endpoint. Binding:
- `{{sources.patient.TenBenhNhan}}` → từ HIS
- `{{sources.labs[0].HbA1c}}` → từ Lakehouse (lấy record mới nhất)

→ **Phần 1 không cần code thêm. Cài đặt hiện có đủ dùng.**

---

## 2. Walkthrough end-to-end — 2 case sử dụng

> Phần này liệt kê **từng bước cụ thể** + **API nào dùng ở mỗi bước** cho 2 case khi onboard 1 nguồn data mới. Đọc trước khi code để hiểu mục tiêu UX cuối cùng.
>
> Base URL local: `http://localhost:5000` (qua nginx gateway). HTTPS: `https://localhost:8443`.

### 2.1 Case A — Nguồn push REST (HIS / BHYT / file Excel)

**Use case minh hoạ:** HIS bệnh viện A tự push từng record bệnh nhân vào Hdos realtime mỗi khi nhập viện.

#### Sơ đồ luồng

```
DE Admin                       External system (HIS)              Hdos
   │                                  │                              │
   │ [Step 1] POST /dm/sources        │                              │
   │ ─────────────────────────────────┼──────────────────────────────►│
   │ ◄─ 201                            │                              │
   │                                  │                              │
   │                                  │ [Step 2] POST /dm/ingest/json│
   │                                  │ (mỗi record mới)             │
   │                                  │ ─────────────────────────────►│
   │                                  │ ◄─ 202                        │
   │                                  │                              │
   │ [Step 3] Verify (đợi ~30s)        │                              │
   │ GET /dm/records?... ─────────────────────────────────────────────►│
   │ ◄─ 200 [...]                                                     │
   │                                                                  │
   │ [Step 4] POST /forms/admin/generate-from-source                  │
   │ ────────────────────────────────────────────────────────────────►│
   │ ◄─ 201                                                            │
   │                                                                  │
   │              [Step 5] FE user mở screen                          │
   │              GET /forms/screens/{m}/{s}/layout                   │
   │              GET /dm/records/{id}                                │
   │              → form pre-fill                                     │
```

#### Step 1 — Đăng ký SourceProfile (1 lần, lúc onboard)

```http
POST http://localhost:5000/dm/sources
Content-Type: application/json

{
  "sourceSystem":     "his-01",
  "recordType":       "benh-nhan",
  "displayName":      "Bệnh nhân (HIS-01)",
  "businessKeyField": "MaBenhNhan",
  "mappings": {
    "ho_ten":        "TenBenhNhan",
    "ngay_sinh":     "NgaySinh",
    "ma_benh_nhan":  "MaBenhNhan",
    "khoa_dieu_tri": "KhoaDieuTri",
    "chan_doan":     "ChanDoan",
    "ngay_nhap_vien":"NgayNhapVien"
  }
}

→ 201 Created
{ "success": true, "data": { "id": "...", "sourceSystem": "his-01", ... } }
```

#### Step 2 — Push data (mỗi record realtime)

External system (HIS) tự gọi sau mỗi sự kiện nhập viện:

```http
POST http://localhost:5000/dm/ingest/json
Content-Type: application/json

{
  "sourceSystem":  "his-01",
  "recordType":    "benh-nhan",
  "payload": {
    "ho_ten":        "Nguyễn Văn An",
    "ngay_sinh":     "1985-03-15",
    "ma_benh_nhan":  "BN2024001",
    "khoa_dieu_tri": "Khoa Tim Mạch",
    "chan_doan":     "Tăng huyết áp độ II",
    "ngay_nhap_vien":"2026-06-01"
  }
}

→ 202 Accepted
{ "success": true, "data": { "id": "rec-uuid", "status": "Pending" } }
```

**Alternative — file batch (migration):**

```http
POST http://localhost:5000/dm/ingest/file
Content-Type: multipart/form-data

file:                <patients.csv hoặc patients.json>
sourceSystem:        his-01
recordType:          benh-nhan
businessKeyOverride: (null)

→ 202 Accepted
{ "success": true, "data": { "count": 1000, "ids": [...] } }
```

#### Step 3 — Verify (đợi ~30s MatchingWorker)

```http
GET http://localhost:5000/dm/records?sourceSystem=his-01&recordType=benh-nhan&limit=10

→ 200 OK
{
  "success": true,
  "data": [
    {
      "id":           "rec-uuid",
      "sourceSystem": "his-01",
      "recordType":   "benh-nhan",
      "businessKey":  "BN2024001",
      "status":       "Matched",
      "canonicalPayload": "{\"TenBenhNhan\":\"Nguyễn Văn An\",\"MaBenhNhan\":\"BN2024001\",...}",
      "receivedAt":   "2026-06-07T...",
      "processedAt":  "2026-06-07T..."
    }
  ]
}
```

#### Step 4 — Auto-generate DynForm screen (1 lần per recordType)

```http
POST http://localhost:5000/forms/admin/generate-from-source
Content-Type: application/json
Authorization: Bearer <token>

{
  "moduleCode":  "datamatch",
  "moduleName":  "DataMatching",
  "screenCode":  "patient-detail",
  "screenTitle": "Hồ sơ bệnh nhân",
  "formKey":     "patient-form",
  "formTitle":   "Thông tin bệnh nhân",
  "dataSource": {
    "namespace":      "record",
    "serviceId":      "datamatch",
    "resourcePath":   "/dm/records/{recordId}",
    "requiredParams": ["recordId"]
  },
  "fields": [
    { "canonicalKey": "MaBenhNhan",  "label": "Mã BN",        "fieldType": "Text" },
    { "canonicalKey": "TenBenhNhan", "label": "Họ tên",       "fieldType": "Text" },
    { "canonicalKey": "NgaySinh",    "label": "Ngày sinh",    "fieldType": "Date",
      "displayFormat": "date:DD/MM/YYYY" },
    { "canonicalKey": "KhoaDieuTri", "label": "Khoa",         "fieldType": "Text" },
    { "canonicalKey": "ChanDoan",    "label": "Chẩn đoán",    "fieldType": "Text" },
    { "canonicalKey": "NgayNhapVien","label": "Ngày NV",      "fieldType": "Date",
      "displayFormat": "date:DD/MM/YYYY" }
  ]
}

→ 201 Created
{
  "moduleCode":      "datamatch",
  "screenCode":      "patient-detail",
  "formKey":         "patient-form",
  "fieldsGenerated": 6
}
```

#### Step 5 — FE user mở screen (auto, không gõ API tay)

User vào URL `/screen?module=datamatch&page=patient-detail&recordId=rec-uuid`. FE tự gọi:

```http
GET /forms/screens/datamatch/patient-detail/layout
→ 200 { dataSources, tabs: [{ widgets: [...] }] }

GET /dm/records/rec-uuid
→ 200 { canonicalPayload: "{...}" }
```

→ FE `useDataSources` parse + spread canonical fields, FormSectionWidget render form pre-fill.

---

### 2.2 Case B1 — Lakehouse view, CÁCH HIỆN TẠI (manual, post Phase 2)

> Đây là tình trạng **trước khi triển khai doc 45**. Admin phải gõ 2 API riêng cho SourceProfile và ViewBinding.

**Use case minh hoạ:** DE đã tạo view `warehouse.v_lab_results_v1` chứa lab results. Admin muốn data này hiển thị trên FE.

#### Sơ đồ luồng

```
DE (Data Engineer)              Admin Hdos                              Hdos
   │                              │                                      │
   │ Tạo VIEW + GRANT SELECT       │                                      │
   │ cho hdos_reader              │                                      │
   │ ─────────────────────────────►│                                      │
   │                              │                                      │
   │                              │ [Step 1] Mở pgAdmin xem schema view  │
   │                              │ (manual, ngoài Hdos)                 │
   │                              │                                      │
   │                              │ [Step 2] POST /dm/sources             │
   │                              │ (gõ mappings tay)                    │
   │                              │ ────────────────────────────────────►│
   │                              │ ◄─ 201                                │
   │                              │                                      │
   │                              │ [Step 3] POST /lakehouse/view-bindings│
   │                              │ ────────────────────────────────────►│
   │                              │ ◄─ 201                                │
   │                              │                                      │
   │                              │ [Step 4] POST /.../{id}/sync          │
   │                              │ ────────────────────────────────────►│
   │                              │ ◄─ 202 (n rows published)             │
   │                              │                                      │
   │                              │ [Step 5] Verify GET /dm/records       │
   │                              │ ────────────────────────────────────►│
   │                              │ ◄─ 200 [...]                          │
   │                              │                                      │
   │                              │ [Step 6] POST /forms/admin/generate.. │
   │                              │ ────────────────────────────────────►│
   │                              │ ◄─ 201                                │
   │                              │                                      │
   │                              │ [Step 7] FE user mở screen           │
```

#### Step 1 — Mở pgAdmin xem schema view (ngoài Hdos)

```sql
-- Chạy thủ công bằng pgAdmin / psql với account có quyền:
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema || '.' || table_name = 'warehouse.v_lab_results_v1'
ORDER BY ordinal_position;
```

→ Admin chép tay danh sách columns ra: `business_key, hba1c, blood_glucose, bmi, updated_at`.

#### Step 2 — Đăng ký SourceProfile (manual)

```http
POST http://localhost:5000/dm/sources
Content-Type: application/json

{
  "sourceSystem":     "lakehouse:v_lab_results_v1",
  "recordType":       "lab-result",
  "displayName":      "Lab Results — Warehouse v1",
  "businessKeyField": "MaBenhNhan",
  "mappings": {
    "business_key":  "MaBenhNhan",
    "hba1c":         "HbA1c",
    "blood_glucose": "Glucose",
    "bmi":           "BMI",
    "updated_at":    "_updated_at"
  }
}

→ 201
{ "success": true, "data": { "id": "...", ... } }
```

> Đau khổ: view 30+ column thì 30+ entry mapping. Mỗi entry phải gõ chính xác cả 2 phía.

#### Step 3 — Đăng ký ViewBinding

```http
POST http://localhost:5000/lakehouse/view-bindings
Content-Type: application/json

{
  "viewName":            "warehouse.v_lab_results_v1",
  "sourceSystem":        "lakehouse:v_lab_results_v1",
  "recordType":          "lab-result",
  "businessKeyColumn":   "business_key",
  "updatedAtColumn":     "updated_at",
  "pollIntervalSeconds": 300
}

→ 201
{ "success": true, "data": { "id": "binding-uuid", ... } }
```

> **Lưu ý quan trọng**: Nếu admin làm Step 3 trước Step 2 (sai thứ tự) → sync sẽ chạy nhưng DataMatching consumer warning "SourceProfile not found" cho từng record → 0 record xuất hiện ở `/dm/records`. Phải Step 2 → Step 3.

#### Step 4 — Trigger sync ngay (không đợi worker)

```http
POST http://localhost:5000/lakehouse/view-bindings/binding-uuid/sync

→ 202
{
  "success": true,
  "data": {
    "bindingId": "binding-uuid",
    "viewName":  "warehouse.v_lab_results_v1",
    "rowCount":  1000,
    "jobId":     "sync-lab-result-20260607...",
    "duration":  "00:00:02.345",
    "error":     null
  }
}
```

#### Step 5 — Verify (đợi ~30s MatchingWorker)

```http
GET http://localhost:5000/dm/records?sourceSystem=lakehouse:v_lab_results_v1&recordType=lab-result&limit=5

→ 200
{
  "success": true,
  "data": [
    {
      "id":               "rec-1",
      "sourceSystem":     "lakehouse:v_lab_results_v1",
      "recordType":       "lab-result",
      "businessKey":      "BN-0001",
      "status":           "Matched",
      "canonicalPayload": "{\"MaBenhNhan\":\"BN-0001\",\"HbA1c\":7.2,\"Glucose\":142,...}"
    }
  ]
}
```

#### Step 6 — Auto-generate DynForm screen

Y hệt Case A Step 4 nhưng đổi field theo canonical của lab-result:

```http
POST /forms/admin/generate-from-source
{
  "moduleCode": "lab",
  "screenCode": "lab-result-detail",
  "formKey":    "lab-result-form",
  "dataSource": {
    "namespace":      "record",
    "resourcePath":   "/dm/records/{recordId}",
    "requiredParams": ["recordId"]
  },
  "fields": [
    { "canonicalKey": "MaBenhNhan", "label": "Mã BN",   "fieldType": "Text"   },
    { "canonicalKey": "HbA1c",      "label": "HbA1c",   "fieldType": "Number" },
    { "canonicalKey": "Glucose",    "label": "Glucose", "fieldType": "Number" },
    { "canonicalKey": "BMI",        "label": "BMI",     "fieldType": "Number" }
  ]
}

→ 201
```

#### Step 7 — FE user mở screen

Y hệt Case A Step 5: FE gọi layout + records, render form.

#### Tổng số API call manual mà admin biết: **5 endpoint khác nhau, 3 lần gõ body JSON dài**

---

### 2.3 Case B2 — Lakehouse view, CÁCH PHASE 2.5 hướng C (preview + compound)

> Đây là target sau khi triển khai doc 45 hướng C. Gộp Step 1-3 của Case B1 thành **2 call duy nhất**.

#### Sơ đồ luồng

```
Admin Hdos                                     Hdos
   │                                            │
   │ [Step 1] GET /.../preview-schema           │
   │ ──────────────────────────────────────────►│
   │                                            │ LakehouseService:
   │                                            │   query warehouse Npgsql
   │                                            │   information_schema.columns
   │                                            │   build suggestedCanonical
   │ ◄─ 200 { columns: [...] }                   │
   │                                            │
   │ [Step 2] Admin (or FE) chỉnh canonical map │
   │                                            │
   │ [Step 3] POST /.../with-profile            │
   │ (body chứa cả binding + profile)            │
   │ ──────────────────────────────────────────►│
   │                                            │ LakehouseService:
   │                                            │   1. POST /dm/sources (qua HTTP)
   │                                            │   2. Tạo ViewBinding cục bộ
   │ ◄─ 201 { binding, profileEnrolled }         │
   │                                            │
   │ [Step 4] POST /.../{id}/sync               │
   │ ──────────────────────────────────────────►│
   │                                            │
   │ Step 5-7 giống Case B1 step 5-7            │
```

#### Step 1 — Preview schema view

```http
GET http://localhost:5000/lakehouse/view-bindings/preview-schema?viewName=warehouse.v_lab_results_v1

→ 200
{
  "success": true,
  "data": {
    "viewName": "warehouse.v_lab_results_v1",
    "columns": [
      {
        "name":                   "business_key",
        "dataType":               "text",
        "nullable":               false,
        "suggestedCanonical":     "MaBenhNhan",
        "isBusinessKeyCandidate": true,
        "isUpdatedAtCandidate":   false
      },
      {
        "name":                   "hba1c",
        "dataType":               "numeric",
        "nullable":               true,
        "suggestedCanonical":     "HbA1c",
        "isBusinessKeyCandidate": false,
        "isUpdatedAtCandidate":   false
      },
      {
        "name":                   "blood_glucose",
        "dataType":               "numeric",
        "nullable":               true,
        "suggestedCanonical":     "Glucose",
        "isBusinessKeyCandidate": false,
        "isUpdatedAtCandidate":   false
      },
      {
        "name":                   "updated_at",
        "dataType":               "timestamptz",
        "nullable":               false,
        "suggestedCanonical":     "_updated_at",
        "isBusinessKeyCandidate": false,
        "isUpdatedAtCandidate":   true
      }
    ]
  }
}
```

> Backend đã tự gợi ý canonical name + đánh dấu column nào nên làm business key / updated-at.

#### Step 2 — Admin chỉnh mapping nếu cần (qua FE)

Admin xem table, chỉnh canonical name cho column nào cần đổi (vd `blood_glucose` → muốn rename thành `LuongDuongHuyet` thay vì `Glucose`). Sau đó submit:

#### Step 3 — Compound create (1 call gộp cả 2)

```http
POST http://localhost:5000/lakehouse/view-bindings/with-profile
Content-Type: application/json

{
  "binding": {
    "viewName":            "warehouse.v_lab_results_v1",
    "sourceSystem":        "lakehouse:v_lab_results_v1",
    "recordType":          "lab-result",
    "businessKeyColumn":   "business_key",
    "updatedAtColumn":     "updated_at",
    "pollIntervalSeconds": 300
  },
  "profile": {
    "displayName":      "Lab Results — Warehouse v1",
    "businessKeyField": "MaBenhNhan",
    "mappings": {
      "business_key":  "MaBenhNhan",
      "hba1c":         "HbA1c",
      "blood_glucose": "Glucose",
      "updated_at":    "_updated_at"
    }
  }
}

→ 201
{
  "success": true,
  "data": {
    "binding": {
      "id":           "binding-uuid",
      "viewName":     "warehouse.v_lab_results_v1",
      "sourceSystem": "lakehouse:v_lab_results_v1",
      "recordType":   "lab-result",
      "isActive":     true,
      ...
    },
    "profileEnrolled": true
  }
}
```

> Behind the scenes: LakehouseService tự gọi `POST http://datamatchingservice:8080/dm/sources` (qua `ISourceProfileEnrollClient` HTTP), nhận 201 hoặc 409 (idempotent) → tạo ViewBinding cục bộ → trả 201 cho admin.

#### Step 4-7 — y hệt Case B1 Step 4-7

```http
POST /lakehouse/view-bindings/binding-uuid/sync   → 202
GET /dm/records?sourceSystem=lakehouse:v_lab_results_v1  → 200
POST /forms/admin/generate-from-source            → 201
... FE auto mở screen ...
```

#### Tổng số API call: **2 endpoint khác nhau (preview + with-profile), 1 lần gõ body JSON** (so với 3 của Case B1)

---

### 2.4 Case B3 — Lakehouse view, MVP hướng B (auto convention)

> Phiên bản nhanh nhất: bỏ luôn preview, backend tự introspect + tự sinh mapping convention.

#### Sơ đồ luồng

```
Admin Hdos                                     Hdos
   │                                            │
   │ [Step 1] POST /.../with-auto-profile        │
   │ ──────────────────────────────────────────►│
   │                                            │ LakehouseService:
   │                                            │   1. Introspect view
   │                                            │   2. Auto sinh mappings
   │                                            │      snake → PascalCase
   │                                            │      + domain overrides
   │                                            │   3. POST /dm/sources
   │                                            │   4. Tạo ViewBinding
   │ ◄─ 201                                      │
   │                                            │
   │ Step 2-5 giống Case B1 step 4-7            │
```

#### Step 1 — 1 call auto (không gõ mappings)

```http
POST http://localhost:5000/lakehouse/view-bindings/with-auto-profile
Content-Type: application/json

{
  "viewName":            "warehouse.v_lab_results_v1",
  "sourceSystem":        "lakehouse:v_lab_results_v1",
  "recordType":          "lab-result",
  "businessKeyColumn":   "business_key",
  "updatedAtColumn":     "updated_at",
  "pollIntervalSeconds": 300,
  "displayName":         "Lab Results — Warehouse v1"
}

→ 201
{
  "success": true,
  "data": {
    "binding": { "id": "...", ... },
    "profile": {
      "enrolled":   true,
      "businessKeyField": "MaBenhNhan",
      "mappings": {
        "business_key":  "MaBenhNhan",
        "hba1c":         "Hba1c",          ← convention auto, đẹp xấu chấp nhận
        "blood_glucose": "BloodGlucose",
        "updated_at":    "_updated_at"
      }
    }
  }
}
```

> Admin **không control** canonical name. Nếu muốn đổi `Hba1c` → `HbA1c` (đẹp hơn) phải gọi API riêng sau:
> ```http
> PUT /dm/sources/{id}  (chưa có UI — Postman)
> ```

#### Step 2-5 — y hệt Case B1 Step 4-7

---

### 2.5 Bảng so sánh số API call

| Case | Số endpoint admin gõ | Số lần gõ body JSON dài | Time onboard 1 view | Admin control mapping |
|---|---|---|---|---|
| **A** (HIS push) | 2 (sources + generate-form) — ingest tự động từ HIS | 2 | 5 phút | N/A |
| **B1** (manual hiện tại) | 4 (sources, view-bindings, sync, generate-form) | 3 | 15-20 phút | ✅ |
| **B2** (Phase 2.5 hướng C) | 3 (preview, with-profile, generate-form) — sync optional | 1 | 5-7 phút | ✅ |
| **B3** (MVP hướng B) | 2 (with-auto-profile, generate-form) | 1 | 3 phút | ❌ phải PUT sửa sau |

### 2.6 Mapping endpoint → service nào sở hữu

| Endpoint | Service xử lý | Phase nào |
|---|---|---|
| `POST /dm/sources` | DataMatchingService | đã có từ trước |
| `POST /dm/ingest/json` / `/file` | DataMatchingService | đã có từ trước |
| `GET /dm/records*` | DataMatchingService | đã có từ trước |
| `POST /lakehouse/view-bindings` | LakehouseService | Phase 2 (Stage 4) |
| `POST /lakehouse/view-bindings/{id}/sync` | LakehouseService | Phase 2 (Stage 4) |
| **`GET /lakehouse/view-bindings/preview-schema`** | **LakehouseService** | **Phase 2.5 (doc này)** |
| **`POST /lakehouse/view-bindings/with-profile`** | **LakehouseService** | **Phase 2.5 (doc này)** |
| **`POST /lakehouse/view-bindings/with-auto-profile`** | **LakehouseService** | **Phase 2.5 (doc này)** |
| `POST /forms/admin/generate-from-source` | DynamicFormService | đã có từ trước |
| `GET /forms/screens/{m}/{s}/layout` | DynamicFormService | đã có từ trước |

### 2.7 Quyết định khi triển khai

**Trước khi code phần Phase 2.5:**
- Nếu chỉ có 1-2 lakehouse view mới mỗi tháng → **Case B1 manual đủ dùng**, không cần code Phase 2.5
- Nếu thêm hàng tuần / cần admin self-service → **code Case B3 (MVP B) trước, ~0.5 ngày**
- Nếu admin cần xem schema view trước khi cấu hình → **code Case B2 (hướng C), ~1.5 ngày**

→ Phần còn lại của tài liệu này (từ section 3 trở đi) cover chi tiết cách code Case B2 + B3.

---

## Phần 2 — Auto-enroll SourceProfile khi tạo ViewBinding

## 4. Vấn đề friction hiện tại

Sau Phase 2, để thêm 1 view lakehouse mới hiển thị trên FE, admin **vẫn phải làm 3 bước thủ công**:

```
[1] Mở pgAdmin xem view có columns gì       (5 phút, cần access pgAdmin)
[2] POST /dm/sources                         (gõ JSON mapping tay, dễ sai)
    {
      "sourceSystem":     "lakehouse:v_lab_results_v1",
      "recordType":       "lab-result",
      "businessKeyField": "MaBenhNhan",
      "mappings": {
        "business_key":  "MaBenhNhan",
        "hba1c":         "HbA1c",
        "blood_glucose": "Glucose",
        "bmi":           "BMI",
        ...30 column khác...
      }
    }
[3] POST /lakehouse/view-bindings            (tạo binding, đã có UI từ Stage 5)
```

**Vấn đề cụ thể:**

| Vấn đề | Hệ quả |
|---|---|
| Admin phải có quyền access warehouse (pgAdmin / DBeaver) để xem schema | Workflow phụ thuộc người khác (DBA) |
| Mapping JSON dài → typo cao | View 30+ column dễ sai, debug khó |
| Bước [2] và [3] dùng 2 service khác nhau → admin không biết thứ tự nào trước | Tạo binding trước → sync fail vì SourceProfile chưa có. Phải re-run |
| Không có gợi ý canonical name | Admin tự nghĩ tên — không nhất quán giữa các bindings |
| 2 transaction tách biệt → có thể orphan | SourceProfile tạo thành công, binding fail → profile orphan |

**Mục tiêu của doc này:** Gộp 3 bước → 1 thao tác. Admin chỉ cần biết view name + cặp `(sourceSystem, recordType)` mong muốn → backend lo phần còn lại.

---

## 5. Ba hướng giải quyết — so sánh

| Hướng | Mô tả | Effort | Khi nào dùng |
|---|---|---|---|
| **A — Identity** | Backend introspect view columns → mapping giữ nguyên tên (snake_case → snake_case) | 0.5 ngày | POC nhanh, chấp nhận canonical name xấu |
| **B — Convention** | Như A nhưng convert `snake_case` → `PascalCase` | 0.5 ngày | MVP, admin sửa mapping sau qua DataMatching admin |
| **C — Preview + Confirm** | Endpoint riêng preview schema → admin chỉnh mapping trong UI → compound create cả 2 | 1.5 ngày | Production, admin có control + visibility |

> **Đề xuất:** đi thẳng **C** vì là feature dài hạn, không phải throw-away. Nếu deadline gấp, làm B trước, refactor C sau (chỉ thêm 1 endpoint preview).

---

## 6. Hướng C — Preview + Compound Create (recommend)

### 6.1 Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Admin FE (sẽ làm trong Stage FE riêng)                          │
│                                                                 │
│  Step 1: Admin gõ viewName "warehouse.v_lab_results_v1"         │
│  Step 2: FE GET /lakehouse/view-bindings/preview-schema         │
│  Step 3: FE hiển thị table 2 cột (raw column | canonical name)  │
│          với suggested canonical pre-fill (BE đề xuất)         │
│  Step 4: Admin chỉnh tay, set businessKeyField                  │
│  Step 5: FE POST /lakehouse/view-bindings/with-profile          │
│          (body chứa cả profile + binding info)                  │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│ LakehouseService                                                │
│                                                                 │
│  [Preview]                                                      │
│  Query Npgsql warehouse:                                        │
│    SELECT column_name, data_type, is_nullable                   │
│    FROM information_schema.columns WHERE ...                    │
│  Sinh suggested canonical name (snake → PascalCase + override   │
│    rules cho field domain quen thuộc)                           │
│                                                                 │
│  [Compound Create]                                              │
│  ┌─────────────────────────────────────────────────────┐       │
│  │ 1. POST http://datamatchingservice:8080/dm/sources  │ ───┐  │
│  │    (qua ISourceProfileEnrollClient HTTP)            │    │  │
│  │    - Nếu 201 → OK                                    │    │  │
│  │    - Nếu 409 (đã tồn tại) → idempotent, OK          │    │  │
│  │    - Nếu khác → fail, không tạo binding             │    │  │
│  └─────────────────────────────────────────────────────┘    │  │
│                                                              │  │
│  2. Tạo ViewBinding cục bộ (Lakehouse DB)                   │  │
│  3. Trả 201 với ViewBindingDto                              │  │
└─────────────────────────────────────────────────────────────┼──┘
                                                              │
                                                              ▼
                                              ┌─────────────────────┐
                                              │ DataMatchingService │
                                              │  POST /dm/sources   │
                                              │  → source_profiles  │
                                              └─────────────────────┘
```

### 6.2 Endpoints mới

#### 6.2.1 `GET /lakehouse/view-bindings/preview-schema`

Đọc metadata view từ warehouse Postgres, trả columns + suggested canonical name.

**Query params:**

| Param | Required | Mô tả |
|---|---|---|
| `viewName` | ✓ | Schema-qualified, vd `warehouse.v_lab_results_v1` |

**Response:**

```json
{
  "success": true,
  "data": {
    "viewName": "warehouse.v_lab_results_v1",
    "columns": [
      {
        "name":               "business_key",
        "dataType":           "text",
        "nullable":           false,
        "suggestedCanonical": "MaBenhNhan",
        "isBusinessKeyCandidate": true,
        "isUpdatedAtCandidate":   false
      },
      {
        "name":               "hba1c",
        "dataType":           "numeric",
        "nullable":           true,
        "suggestedCanonical": "HbA1c",
        "isBusinessKeyCandidate": false,
        "isUpdatedAtCandidate":   false
      },
      {
        "name":               "updated_at",
        "dataType":           "timestamptz",
        "nullable":           false,
        "suggestedCanonical": "_updated_at",
        "isBusinessKeyCandidate": false,
        "isUpdatedAtCandidate":   true
      }
    ]
  }
}
```

**Error:**
- `404 NotFound` — view không tồn tại / hdos_reader không có quyền SELECT
- `400 Validation` — viewName không match format `schema.table_name`

#### 6.2.2 `POST /lakehouse/view-bindings/with-profile`

Compound: tạo SourceProfile (cross-service) + ViewBinding (cục bộ) trong 1 call.

**Body:**

```json
{
  "binding": {
    "viewName":            "warehouse.v_lab_results_v1",
    "sourceSystem":        "lakehouse:v_lab_results_v1",
    "recordType":          "lab-result",
    "businessKeyColumn":   "business_key",
    "updatedAtColumn":     "updated_at",
    "pollIntervalSeconds": 300
  },
  "profile": {
    "displayName":      "Lab Results — Warehouse v1",
    "businessKeyField": "MaBenhNhan",
    "mappings": {
      "business_key":  "MaBenhNhan",
      "hba1c":         "HbA1c",
      "blood_glucose": "Glucose"
    }
  }
}
```

**Response 201:**

```json
{
  "success": true,
  "data": {
    "binding": { "id": "...", "viewName": "...", ... },
    "profile": { "sourceSystem": "...", "recordType": "...", "enrolled": true }
  }
}
```

**Error:**
- `400 Validation` — body sai schema, businessKeyField không nằm trong values của mappings, mappings duplicate canonical
- `502 BadGateway` — DataMatching không reachable
- `500` — DataMatching enroll fail với non-409 status

### 6.3 File layout backend

```
src/Services/LakehouseService/
├── LakehouseService.Application/
│   ├── DTOs/
│   │   └── ViewSchemaDto.cs                          ← MỚI
│   ├── Features/
│   │   └── ViewBindings/
│   │       ├── PreviewSchema/                        ← MỚI
│   │       │   ├── PreviewSchemaQuery.cs
│   │       │   └── PreviewSchemaQueryHandler.cs
│   │       └── CreateWithProfile/                    ← MỚI
│   │           ├── CreateBindingWithProfileCommand.cs
│   │           ├── CreateBindingWithProfileHandler.cs
│   │           └── CreateBindingWithProfileValidator.cs
│   └── Services/                                     ← MỚI thư mục
│       └── ISourceProfileEnrollClient.cs
├── LakehouseService.Infrastructure/
│   └── ExternalClients/                              ← MỚI thư mục
│       └── SourceProfileEnrollClient.cs
│   └── DependencyInjection.cs                        ← THÊM AddHttpClient
└── LakehouseService.API/
    └── Controllers/
        └── ViewBindingsController.cs                 ← THÊM 2 endpoint
```

### 6.4 Code mẫu

#### 6.4.1 ViewSchemaDto

```csharp
// LakehouseService.Application/DTOs/ViewSchemaDto.cs
namespace Hdos.LakehouseService.Application.DTOs;

public sealed record ViewSchemaDto(
    string                    ViewName,
    List<ViewColumnInfoDto>   Columns);

public sealed record ViewColumnInfoDto(
    string Name,
    string DataType,
    bool   Nullable,
    string SuggestedCanonical,
    bool   IsBusinessKeyCandidate,
    bool   IsUpdatedAtCandidate);
```

#### 6.4.2 PreviewSchemaQuery + Handler

```csharp
// LakehouseService.Application/Features/ViewBindings/PreviewSchema/PreviewSchemaQuery.cs
using FluentValidation;
using Hdos.LakehouseService.Application.DTOs;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.LakehouseService.Application.Features.ViewBindings.PreviewSchema;

public sealed record PreviewSchemaQuery(string ViewName) : IRequest<Result<ViewSchemaDto>>;

public sealed class PreviewSchemaQueryValidator : AbstractValidator<PreviewSchemaQuery>
{
    public PreviewSchemaQueryValidator()
    {
        RuleFor(x => x.ViewName)
            .NotEmpty()
            .Matches(@"^[a-zA-Z_][\w]*\.[a-zA-Z_][\w]*$")
            .WithMessage("ViewName phải có dạng 'schema.view_name'.");
    }
}
```

```csharp
// LakehouseService.Application/Features/ViewBindings/PreviewSchema/PreviewSchemaQueryHandler.cs
using Hdos.LakehouseService.Application.DTOs;
using Hdos.SharedKernel;
using MediatR;
using Npgsql;

namespace Hdos.LakehouseService.Application.Features.ViewBindings.PreviewSchema;

public sealed class PreviewSchemaQueryHandler(NpgsqlDataSource warehouseDs)
    : IRequestHandler<PreviewSchemaQuery, Result<ViewSchemaDto>>
{
    private const string Sql = """
        SELECT column_name, data_type, is_nullable
        FROM information_schema.columns
        WHERE table_schema = @schema AND table_name = @table
        ORDER BY ordinal_position
        """;

    public async Task<Result<ViewSchemaDto>> Handle(PreviewSchemaQuery req, CancellationToken ct)
    {
        var parts  = req.ViewName.Split('.');
        var schema = parts[0];
        var table  = parts[1];

        await using var conn = await warehouseDs.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table",  table);

        var cols = new List<ViewColumnInfoDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name     = reader.GetString(0);
            var dataType = reader.GetString(1);
            var nullable = reader.GetString(2) == "YES";

            cols.Add(new ViewColumnInfoDto(
                Name:                   name,
                DataType:               dataType,
                Nullable:               nullable,
                SuggestedCanonical:     SuggestCanonical(name),
                IsBusinessKeyCandidate: IsBusinessKeyName(name) && !nullable,
                IsUpdatedAtCandidate:   IsTimestampColumn(name, dataType) && !nullable));
        }

        if (cols.Count == 0)
            return Result.Failure<ViewSchemaDto>(
                Error.NotFound($"View '{req.ViewName}' hoặc hdos_reader thiếu quyền SELECT"));

        return new ViewSchemaDto(req.ViewName, cols);
    }

    // Convention: domain healthcare known columns → PascalCase domain name
    // Fallback: snake_case → PascalCase
    private static string SuggestCanonical(string name) => name.ToLowerInvariant() switch
    {
        "business_key" or "patient_id" or "ma_benh_nhan" => "MaBenhNhan",
        "patient_name" or "ho_ten" or "full_name"        => "TenBenhNhan",
        "date_of_birth" or "ngay_sinh" or "dob"          => "NgaySinh",
        "department"   or "khoa"      or "khoa_dieu_tri" => "KhoaDieuTri",
        "diagnosis"    or "chan_doan"                    => "ChanDoan",
        "admission_date" or "ngay_nhap_vien"             => "NgayNhapVien",
        "created_at"                                     => "_created_at",
        "updated_at"                                     => "_updated_at",
        _ => SnakeToPascal(name)
    };

    private static string SnakeToPascal(string snake) =>
        string.Concat(snake.Split('_')
            .Where(p => p.Length > 0)
            .Select(p => char.ToUpper(p[0]) + p[1..]));

    private static bool IsBusinessKeyName(string name) =>
        name.Equals("business_key",   StringComparison.OrdinalIgnoreCase) ||
        name.Equals("patient_id",     StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ma_benh_nhan",   StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("_id",          StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("_key",         StringComparison.OrdinalIgnoreCase);

    private static bool IsTimestampColumn(string name, string dataType) =>
        (dataType.StartsWith("timestamp") || dataType == "date") &&
        (name.EndsWith("_at", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith("_time", StringComparison.OrdinalIgnoreCase));
}
```

#### 6.4.3 ISourceProfileEnrollClient

```csharp
// LakehouseService.Application/Services/ISourceProfileEnrollClient.cs
using Hdos.SharedKernel;

namespace Hdos.LakehouseService.Application.Services;

public interface ISourceProfileEnrollClient
{
    /// <summary>
    /// Đăng ký (hoặc xác nhận đã có) SourceProfile bên DataMatchingService.
    /// Idempotent: nếu profile đã tồn tại, trả Success — không fail.
    /// </summary>
    Task<Result> EnrollAsync(SourceProfileEnrollRequest req, CancellationToken ct);
}

public sealed record SourceProfileEnrollRequest(
    string                     SourceSystem,
    string                     RecordType,
    string                     DisplayName,
    string                     BusinessKeyField,
    Dictionary<string, string> Mappings);
```

#### 6.4.4 SourceProfileEnrollClient (HTTP)

```csharp
// LakehouseService.Infrastructure/ExternalClients/SourceProfileEnrollClient.cs
using System.Net;
using System.Net.Http.Json;
using Hdos.LakehouseService.Application.Services;
using Hdos.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Hdos.LakehouseService.Infrastructure.ExternalClients;

public sealed class SourceProfileEnrollClient(
    HttpClient                              http,
    ILogger<SourceProfileEnrollClient>      logger)
    : ISourceProfileEnrollClient
{
    public async Task<Result> EnrollAsync(SourceProfileEnrollRequest req, CancellationToken ct)
    {
        try
        {
            var resp = await http.PostAsJsonAsync("/dm/sources", new
            {
                sourceSystem     = req.SourceSystem,
                recordType       = req.RecordType,
                displayName      = req.DisplayName,
                businessKeyField = req.BusinessKeyField,
                mappings         = req.Mappings,
            }, ct);

            if (resp.IsSuccessStatusCode) return Result.Success();

            // Idempotent: 409 (đã tồn tại) coi như OK
            if (resp.StatusCode == HttpStatusCode.Conflict)
            {
                logger.LogInformation(
                    "SourceProfile {Src}/{Type} đã tồn tại bên DataMatching — skip enroll",
                    req.SourceSystem, req.RecordType);
                return Result.Success();
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "DataMatching enroll {Src}/{Type} failed: HTTP {Status} — {Body}",
                req.SourceSystem, req.RecordType, (int)resp.StatusCode, body);

            return Result.Failure(Error.Validation(
                $"DataMatching enroll fail ({(int)resp.StatusCode}): {body}"));
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error enroll SourceProfile {Src}/{Type}",
                req.SourceSystem, req.RecordType);
            return Result.Failure(Error.Validation(
                "Không kết nối được DataMatchingService — thử lại sau."));
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Timeout enroll SourceProfile {Src}/{Type}",
                req.SourceSystem, req.RecordType);
            return Result.Failure(Error.Validation(
                "Timeout khi gọi DataMatchingService."));
        }
    }
}
```

#### 6.4.5 CreateBindingWithProfileCommand + Handler

```csharp
// LakehouseService.Application/Features/ViewBindings/CreateWithProfile/CreateBindingWithProfileCommand.cs
using FluentValidation;
using Hdos.LakehouseService.Application.DTOs;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.LakehouseService.Application.Features.ViewBindings.CreateWithProfile;

public sealed record CreateBindingWithProfileCommand(
    BindingPart Binding,
    ProfilePart Profile) : IRequest<Result<CreateBindingWithProfileResultDto>>;

public sealed record BindingPart(
    string ViewName,
    string SourceSystem,
    string RecordType,
    string BusinessKeyColumn,
    string UpdatedAtColumn,
    int    PollIntervalSeconds);

public sealed record ProfilePart(
    string                     DisplayName,
    string                     BusinessKeyField,
    Dictionary<string, string> Mappings);

public sealed record CreateBindingWithProfileResultDto(
    ViewBindingDto Binding,
    bool           ProfileEnrolled);
```

```csharp
// Validator
public sealed class CreateBindingWithProfileValidator
    : AbstractValidator<CreateBindingWithProfileCommand>
{
    public CreateBindingWithProfileValidator()
    {
        RuleFor(x => x.Binding.ViewName).NotEmpty()
            .Matches(@"^[a-zA-Z_][\w]*\.[a-zA-Z_][\w]*$");
        RuleFor(x => x.Binding.SourceSystem).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Binding.RecordType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Binding.BusinessKeyColumn).NotEmpty();
        RuleFor(x => x.Binding.UpdatedAtColumn).NotEmpty();
        RuleFor(x => x.Binding.PollIntervalSeconds).GreaterThanOrEqualTo(30);

        RuleFor(x => x.Profile.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Profile.BusinessKeyField).NotEmpty();
        RuleFor(x => x.Profile.Mappings)
            .NotEmpty()
            .Must(m => m.Values.GroupBy(v => v).All(g => g.Count() == 1))
            .WithMessage("Mappings có canonical name trùng nhau.")
            .Must((cmd, m) => m.Values.Contains(cmd.Profile.BusinessKeyField))
            .WithMessage("BusinessKeyField phải nằm trong values của mappings.");
        RuleFor(x => x)
            .Must(x => x.Profile.Mappings.ContainsKey(x.Binding.BusinessKeyColumn))
            .WithMessage("BusinessKeyColumn của binding phải có entry trong mappings.");
    }
}
```

```csharp
// Handler
using Hdos.LakehouseService.Application.DTOs;
using Hdos.LakehouseService.Application.Services;
using Hdos.LakehouseService.Domain.Entities;
using Hdos.LakehouseService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hdos.LakehouseService.Application.Features.ViewBindings.CreateWithProfile;

public sealed class CreateBindingWithProfileHandler(
    ISourceProfileEnrollClient                       profileClient,
    IViewBindingRepository                           bindings,
    ILogger<CreateBindingWithProfileHandler>         logger)
    : IRequestHandler<CreateBindingWithProfileCommand, Result<CreateBindingWithProfileResultDto>>
{
    public async Task<Result<CreateBindingWithProfileResultDto>> Handle(
        CreateBindingWithProfileCommand cmd, CancellationToken ct)
    {
        // [1] Check view binding chưa tồn tại — fail nhanh trước khi enroll
        var existing = await bindings.GetByViewNameAsync(cmd.Binding.ViewName, ct);
        if (existing is not null)
            return Result.Failure<CreateBindingWithProfileResultDto>(
                Error.Conflict($"ViewBinding cho '{cmd.Binding.ViewName}' đã tồn tại."));

        // [2] Enroll SourceProfile bên DataMatching (idempotent — 409 OK)
        var enroll = await profileClient.EnrollAsync(new SourceProfileEnrollRequest(
            SourceSystem:     cmd.Binding.SourceSystem,
            RecordType:       cmd.Binding.RecordType,
            DisplayName:      cmd.Profile.DisplayName,
            BusinessKeyField: cmd.Profile.BusinessKeyField,
            Mappings:         cmd.Profile.Mappings), ct);

        if (enroll.IsFailure)
            return Result.Failure<CreateBindingWithProfileResultDto>(enroll.Error);

        // [3] Tạo ViewBinding cục bộ
        var binding = ViewBinding.Create(
            cmd.Binding.ViewName,
            cmd.Binding.SourceSystem,
            cmd.Binding.RecordType,
            cmd.Binding.BusinessKeyColumn,
            cmd.Binding.UpdatedAtColumn,
            cmd.Binding.PollIntervalSeconds);

        await bindings.AddAsync(binding, ct);
        await bindings.SaveChangesAsync(ct);

        logger.LogInformation(
            "ViewBinding + SourceProfile enrolled for {View} ({Src}/{Type})",
            binding.ViewName, binding.SourceSystem, binding.RecordType);

        var dto = new ViewBindingDto(
            binding.Id, binding.ViewName, binding.SourceSystem, binding.RecordType,
            binding.BusinessKeyColumn, binding.UpdatedAtColumn, binding.PollIntervalSeconds,
            binding.IsActive, binding.CreatedAtUtc, binding.UpdatedAtUtc);

        return new CreateBindingWithProfileResultDto(dto, ProfileEnrolled: true);
    }
}
```

#### 6.4.6 Controller — thêm 2 endpoint

```csharp
// LakehouseService.API/Controllers/ViewBindingsController.cs (thêm vào class hiện có)

/// <summary>Preview schema của 1 view trong warehouse — gợi ý canonical mapping.</summary>
[HttpGet("preview-schema")]
[ProducesResponseType(typeof(ApiResponse<ViewSchemaDto>), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
public async Task<IActionResult> PreviewSchema([FromQuery] string viewName, CancellationToken ct)
{
    var result = await sender.Send(new PreviewSchemaQuery(viewName), ct);
    return result.IsSuccess
        ? Ok(ApiResponse<ViewSchemaDto>.Ok(result.Value))
        : NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
}

/// <summary>Tạo binding mới + auto enroll SourceProfile sang DataMatchingService.</summary>
/// <response code="201">Cả binding + profile đều sẵn sàng.</response>
/// <response code="400">Validation fail (mappings sai, businessKey không match, ...).</response>
/// <response code="409">Binding cho view này đã tồn tại.</response>
/// <response code="502">DataMatching không reachable / enroll fail.</response>
[HttpPost("with-profile")]
[ProducesResponseType(typeof(ApiResponse<CreateBindingWithProfileResultDto>), StatusCodes.Status201Created)]
[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status502BadGateway)]
public async Task<IActionResult> CreateWithProfile(
    [FromBody] CreateBindingWithProfileCommand cmd, CancellationToken ct)
{
    var result = await sender.Send(cmd, ct);
    if (result.IsSuccess)
        return Created(string.Empty, ApiResponse<CreateBindingWithProfileResultDto>.Ok(result.Value));

    return result.Error.Code switch
    {
        "Conflict"   => Conflict(ApiResponse.Fail(result.Error.Code, result.Error.Message)),
        "Validation" => result.Error.Message.Contains("DataMatching") || result.Error.Message.Contains("Timeout")
                            ? StatusCode(502, ApiResponse.Fail(result.Error.Code, result.Error.Message))
                            : BadRequest(ApiResponse.Fail(result.Error.Code, result.Error.Message)),
        _            => BadRequest(ApiResponse.Fail(result.Error.Code, result.Error.Message))
    };
}
```

### 6.5 Register DI

```csharp
// LakehouseService.Infrastructure/DependencyInjection.cs (thêm vào AddLakehouseInfrastructure)

services.AddHttpClient<ISourceProfileEnrollClient, SourceProfileEnrollClient>(c =>
{
    c.BaseAddress = new Uri(
        configuration["Services:DataMatching:BaseUrl"]
            ?? "http://datamatchingservice:8080");
    c.Timeout = TimeSpan.FromSeconds(10);
});
```

---

## 7. Hướng B — Auto-convention (MVP nhanh)

Nếu chưa cần preview UI, làm phiên bản tự động hoàn toàn:

### 7.1 1 endpoint duy nhất

```http
POST /lakehouse/view-bindings/with-auto-profile
{
  "viewName":            "warehouse.v_lab_results_v1",
  "sourceSystem":        "lakehouse:v_lab_results_v1",
  "recordType":          "lab-result",
  "businessKeyColumn":   "business_key",
  "updatedAtColumn":     "updated_at",
  "pollIntervalSeconds": 300,
  "displayName":         "Lab Results — Warehouse v1"
}
```

Backend:
1. Introspect view (như §6.4.2)
2. Build mappings tự động: mỗi column → `SuggestCanonical(name)` (cùng helper)
3. Set `businessKeyField = SuggestCanonical(businessKeyColumn)`
4. Enroll qua `ISourceProfileEnrollClient`
5. Tạo ViewBinding

→ 0 click chỉnh mapping. Admin sửa sau qua API `PUT /dm/sources/{id}` (chưa có UI).

### 7.2 So với hướng C

| Tiêu chí | B (auto) | C (preview + confirm) |
|---|---|---|
| Effort | 0.5 ngày | 1.5 ngày |
| Admin click | 1 | 3 (gõ viewName → xem schema → confirm) |
| Sửa mapping sau | Bắt buộc (đa số case) | Hiếm khi cần |
| FE work cần | 0 | 1 trang preview |
| Production-ready | ❌ tạm | ✅ |

**Khuyến nghị**: Code B trước (1 ngày BE), dùng 2 tuần lấy feedback, sau đó upgrade lên C nếu admin complain. C reuse được 80% code của B.

---

## 8. Cấu hình inter-service

### 8.1 Environment variable

Thêm vào `docker-compose.yml` cho `lakehouseservice`:

```yaml
lakehouseservice:
  environment:
    Services__DataMatching__BaseUrl: "http://datamatchingservice:8080"
    # ... existing env
```

Trong production thay bằng URL thực hoặc nginx internal endpoint.

### 8.2 Auth giữa 2 service

**Hôm nay:** Internal HTTP call **không có auth**. Cùng docker network, RabbitMQ + Postgres cũng không auth giữa services.

**Tương lai (nếu cần):** Mỗi service mang `X-Internal-Token` header, secret share qua env. Hoặc dùng mTLS / service mesh.

Tài liệu này không scope phần auth — coi như trusted internal network.

### 8.3 Timeout + Retry

`HttpClient` config:
- Timeout 10s — đủ cho enroll (chỉ 1 INSERT + bảng nhỏ)
- Không retry tự động (idempotent ở backend, retry là việc của admin)

Nếu cần retry, thêm Polly:

```csharp
services.AddHttpClient<ISourceProfileEnrollClient, SourceProfileEnrollClient>(c => {...})
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3,
        attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
```

---

## 9. Edge cases + quyết định kỹ thuật

| Tình huống | Quyết định | Lý do |
|---|---|---|
| DataMatching đã có SourceProfile cho `(src, type)` này từ trước | Idempotent — 409 → coi như OK, vẫn tạo ViewBinding | Admin có thể đã chạy POST /dm/sources manual trước rồi mới dùng UI |
| Mapping mới khác mapping cũ của SourceProfile đã tồn tại | **KHÔNG ghi đè** (DataMatching hiện không support upsert). Admin phải DELETE profile cũ trước hoặc gọi `PUT /dm/sources/{id}` riêng | Tránh ghi đè ngầm, an toàn data |
| ViewBinding tạo OK nhưng enroll SourceProfile fail (vd network blip) | Toàn bộ transaction fail — không có binding/profile nào được tạo | Tránh orphan |
| Enroll SourceProfile OK nhưng ViewBinding fail (vd duplicate view name check chậm) | Profile orphan ở DataMatching | Acceptable — admin retry sẽ trả 409 (idempotent), không hại. Có thể xoá manual nếu cần |
| View không tồn tại trong warehouse | `404 NotFound` ở preview, `400` ở compound create | Validation nên fail trước khi enroll |
| `hdos_reader` chưa được GRANT trên view mới | Như "không tồn tại" — `information_schema.columns` trả empty | Lỗi rõ → admin yêu cầu DE grant |
| View có column name chứa ký tự đặc biệt (vd `weight (kg)`) | Suggest canonical bằng SnakeToPascal có thể lỗi | Validator regex `^[a-zA-Z_][\w]*$`. Nếu fail → admin tự gõ canonical name |
| View có >100 columns | Hoạt động bình thường nhưng UI preview cần scroll | FE hiển thị virtual scroll table |
| 2 admin race condition tạo cùng view | Database unique index `IX_ViewBindings_ViewName` chặn → admin thứ 2 nhận 409 | Đã có từ Stage 4 |
| Mapping rỗng | Validator fail | Mappings là bắt buộc |
| BusinessKeyField không nằm trong values mappings | Validator fail trước khi enroll | Catch sớm |

---

## 10. Implementation order

### Phase 1 — MVP (Hướng B, 0.5–1 ngày)

```
[1] Common (cần cho cả B + C)
    [ ] Tạo ISourceProfileEnrollClient + SourceProfileEnrollClient
    [ ] Thêm AddHttpClient trong DependencyInjection
    [ ] Thêm env Services__DataMatching__BaseUrl vào docker-compose

[2] B — Auto endpoint
    [ ] Tạo Features/ViewBindings/CreateWithAutoProfile/
        [ ] Command + Validator + Handler
        [ ] Reuse SuggestCanonical helper (đặt static class chung Helpers/)
    [ ] Thêm endpoint POST /lakehouse/view-bindings/with-auto-profile vào controller
    [ ] dotnet build + test manual qua Postman

[3] Verify end-to-end
    [ ] Tạo 1 view test trong warehouse-postgres (xem doc 43 §3.2)
    [ ] Gọi POST /lakehouse/view-bindings/with-auto-profile
    [ ] GET /dm/sources → confirm profile đã enroll
    [ ] GET /lakehouse/view-bindings → confirm binding tạo
    [ ] Trigger sync → record xuất hiện ở /dm/records
```

### Phase 2 — Production (Hướng C, +1 ngày)

```
[4] Preview endpoint
    [ ] Tạo Features/ViewBindings/PreviewSchema/ (Query + Validator + Handler)
    [ ] Thêm GET /lakehouse/view-bindings/preview-schema vào controller

[5] Compound create endpoint
    [ ] Tạo Features/ViewBindings/CreateWithProfile/ (Command + Validator + Handler)
    [ ] Thêm POST /lakehouse/view-bindings/with-profile vào controller
    [ ] Reuse client + helper từ Phase 1

[6] FE work (out-of-scope BE, ghi spec riêng)
    [ ] Trang preview schema + form chỉnh mapping
    [ ] Submit compound create
    [ ] Update sidebar/admin nav
```

---

## 11. Testing checklist

### 11.1 Unit / integration BE

```
[ ] PreviewSchemaQueryHandler:
    [ ] View tồn tại → trả columns + suggested names
    [ ] View không tồn tại → NotFound
    [ ] hdos_reader thiếu quyền → NotFound (empty columns)
    [ ] ViewName format sai → Validator fail
    [ ] business_key column → IsBusinessKeyCandidate = true
    [ ] timestamptz column tên kết thúc _at → IsUpdatedAtCandidate = true

[ ] SourceProfileEnrollClient:
    [ ] Enroll thành công 201 → Result.Success
    [ ] Enroll 409 đã tồn tại → Result.Success (idempotent)
    [ ] Enroll 400 → Result.Failure với error.message từ BE
    [ ] Network down → Result.Failure (HttpRequestException)
    [ ] Timeout 10s → Result.Failure (TaskCanceledException)

[ ] CreateBindingWithProfileHandler:
    [ ] Happy path → enroll OK + binding tạo OK
    [ ] ViewBinding đã tồn tại → Conflict, không enroll
    [ ] Enroll fail → không tạo binding, return error
    [ ] Mappings duplicate canonical → Validator fail
    [ ] BusinessKeyField không trong mappings → Validator fail
    [ ] BusinessKeyColumn không có entry trong mappings → Validator fail
```

### 11.2 End-to-end manual

```
[ ] docker compose up -d
[ ] Đảm bảo warehouse-postgres có view test (xem doc 43 §3)
[ ] Curl preview:
    curl 'http://localhost:5000/lakehouse/view-bindings/preview-schema?viewName=warehouse.v_lab_results_v1'
    → expect: 200 với suggestedCanonical pre-fill
[ ] Curl compound create:
    curl -X POST http://localhost:5000/lakehouse/view-bindings/with-profile \
      -H 'Content-Type: application/json' \
      -d '{
        "binding": { ... },
        "profile": { ... }
      }'
    → expect: 201
[ ] Verify ở DataMatching:
    curl 'http://localhost:5000/dm/sources' → có profile mới
[ ] Verify ở Lakehouse:
    curl 'http://localhost:5000/lakehouse/view-bindings' → có binding mới
[ ] Trigger sync:
    curl -X POST http://localhost:5000/lakehouse/view-bindings/{id}/sync
    → expect: 202 + rowCount > 0
[ ] Verify ở DataMatching records:
    curl 'http://localhost:5000/dm/records?sourceSystem=lakehouse:v_lab_results_v1'
    → expect: data với canonical fields đã rename theo mapping
```

### 11.3 Re-run (idempotent)

```
[ ] Tạo lại với cùng viewName → 409 Conflict (binding-level dedup)
[ ] Xoá binding, giữ profile, tạo lại → 201 (profile enroll trả 409 idempotent → OK)
[ ] Xoá profile manual qua DataMatching admin, tạo lại binding → 201 + profile được re-enroll
```

---

## Liên quan

- [22 — CDC Debezium](./22-cdc-debezium-kafka.md) — alternative realtime
- [23 — DataMatchingService](./23-data-matching-service.md) — service nhận enroll
- [36 — DataMatch → DynForm](./36-datamatch-to-dynform-flow.md) — flow auto-gen form sau khi có SourceProfile
- [43 — Warehouse Sync](./43-warehouse-sync-to-lakehouse.md) — pull view pattern
- [44 — Unified Ingest Pipeline](./44-unified-ingest-pipeline.md) — kiến trúc Phase 2 (ViewBinding gốc)
- `fe/FOXAI-HDOSv2/docs/view-bindings-admin-ui.md` — spec FE Stage 5 (mở rộng được để gọi 2 endpoint mới)

---

## Note kết

Sau khi triển khai Phase 1 (hướng B):
- Onboard 1 view lakehouse mới = 1 HTTP call duy nhất
- Admin không cần access pgAdmin
- Mapping convention nhất quán (snake_case → PascalCase + domain overrides)
- Trade-off: mapping mặc định có thể chưa hoàn hảo (vd `hba1c` → `Hba1c` thay vì `HbA1c`) — admin sửa qua API riêng

Sau khi triển khai Phase 2 (hướng C):
- Admin control mapping 100%, không phụ thuộc convention
- Visibility schema view tích hợp ngay trong workflow
- Production-grade UX

Cả 2 phase đều **tương thích ngược** với endpoints `POST /lakehouse/view-bindings` (manual) và `POST /dm/sources` (manual) hiện có — không break flow cũ.
