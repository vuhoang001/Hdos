# 46 — Playbook: Thêm Nguồn Data và Hiển Thị FE

> **Mục đích.** Hướng dẫn thực hành **copy-paste được luôn** cho admin/dev: 2 cách thêm 1 nguồn data mới vào Hdos và hiển thị qua DynamicForm động — Cách 1 (push) cho HIS/BHYT, Cách 2 (lakehouse view) cho data đã sẵn trong warehouse.
>
> Khác với doc 44 (kiến trúc) và doc 45 (implementation chi tiết), doc này là **playbook step-by-step** — đọc xong là làm được.

**Đối tượng đọc:** admin / dev mới onboarding / QA. Không cần đọc trước doc 44, 45 (nhưng nếu cần hiểu sâu thì xem mục [Liên quan](#liên-quan) ở cuối).

---

## Mục lục

1. [Khi nào dùng cách nào](#1-khi-nào-dùng-cách-nào)
2. [Setup local trước khi bắt đầu](#2-setup-local-trước-khi-bắt-đầu)
3. [Cách 1 — DataMatching push (HIS / BHYT / file)](#3-cách-1--datamatching-push-his--bhyt--file)
4. [Cách 2 — Lakehouse view (auto-enroll qua MVP B)](#4-cách-2--lakehouse-view-auto-enroll-qua-mvp-b)
5. [So sánh 2 cách](#5-so-sánh-2-cách)
6. [Pitfalls — lỗi hay gặp + fix](#6-pitfalls--lỗi-hay-gặp--fix)
7. [Thêm nguồn data thứ 3, 4, 5...](#7-thêm-nguồn-data-thứ-3-4-5)
8. [Tóm lại — quy tắc vàng](#8-tóm-lại--quy-tắc-vàng)

---

## 1. Khi nào dùng cách nào

| Tình huống | Cách phù hợp |
|---|---|
| HIS/BHYT/EMR có khả năng tự gọi REST API mỗi khi có record mới | **Cách 1 — push realtime** |
| Có file CSV/Excel dump định kỳ, admin upload tay | **Cách 1 — file batch** |
| Data đã được DE team load vào PostgreSQL warehouse (qua ETL pipeline) | **Cách 2 — lakehouse view** |
| Data analytics (aggregated, BI cube) trong warehouse | **Cách 2 — lakehouse view** |
| Cần realtime < 5 giây | Không cách nào ở đây — xem [doc 22 CDC](./22-cdc-debezium-kafka.md) |

**Quy tắc thực tế:**
- Source **tự đẩy data** → Cách 1.
- Source **để data tự nằm trong DB**, Hdos đến lấy → Cách 2.

---

## 2. Setup local trước khi bắt đầu

```bash
cd /home/hoanggggf/Documents/Code/work/Hdos
docker compose up -d

# Verify mọi service đang chạy
curl http://localhost:5000/health
```

Sau bước này có sẵn:

| Service | Endpoint |
|---|---|
| DataMatchingService | `http://localhost:5000/dm/*` |
| LakehouseService | `http://localhost:5000/lakehouse/*` |
| DynamicFormService | `http://localhost:5000/forms/*` |
| RabbitMQ UI | `http://localhost:15672` (guest/guest) |
| FE | `http://localhost:5000/` |

---

## 3. Cách 1 — DataMatching push (HIS / BHYT / file)

### Bước 1.1 — Đăng ký SourceProfile

Một lần / 1 cặp `(sourceSystem, recordType)`. Định nghĩa cách rename field raw → canonical.

```bash
curl -X POST http://localhost:5000/dm/sources \
  -H 'Content-Type: application/json' \
  -d '{
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
  }'
```

Response 201 → SourceProfile lưu trong DataMatching DB.

### Bước 1.2 — Push record realtime hoặc batch file

**Option A — push 1 record (external system tự gọi):**

```bash
curl -X POST http://localhost:5000/dm/ingest/json \
  -H 'Content-Type: application/json' \
  -d '{
    "sourceSystem":  "his-01",
    "recordType":    "benh-nhan",
    "payload": {
      "ho_ten":         "Nguyễn Văn An",
      "ngay_sinh":      "1985-03-15",
      "ma_benh_nhan":   "BN2024001",
      "khoa_dieu_tri":  "Khoa Tim Mạch",
      "chan_doan":      "Tăng huyết áp độ II",
      "ngay_nhap_vien": "2026-06-01"
    }
  }'
```

Response 202: `{ "id": "rec-uuid", "status": "Pending" }`. Sau ~30s `MatchingWorker` chạy → status `Matched`.

**Option B — batch file (admin upload):**

```bash
curl -X POST http://localhost:5000/dm/ingest/file \
  -F 'file=@patients.csv' \
  -F 'sourceSystem=his-01' \
  -F 'recordType=benh-nhan'
```

CSV/JSON đều OK. Mỗi dòng/object = 1 record. Field name phải khớp `mappings.keys` đã đăng ký ở Bước 1.1.

### Bước 1.3 — Verify data đã canonicalize

```bash
# List tất cả của 1 source
curl 'http://localhost:5000/dm/records?sourceSystem=his-01&recordType=benh-nhan&limit=5'

# Lấy 1 record cụ thể
curl 'http://localhost:5000/dm/records/<rec-uuid>'
```

Expected:

```json
{
  "id":           "rec-uuid",
  "sourceSystem": "his-01",
  "recordType":   "benh-nhan",
  "businessKey":  "BN2024001",
  "status":       "Matched",
  "canonicalPayload": "{\"TenBenhNhan\":\"Nguyễn Văn An\",\"MaBenhNhan\":\"BN2024001\",...}"
}
```

→ Field đã rename từ `ho_ten` thành `TenBenhNhan`. Sẵn sàng cho FE.

### Bước 1.4 — Auto-gen DynForm screen

Một lần / 1 màn hình. Sinh Screen + DataSource + FormSection widget + Fields:

```bash
curl -X POST http://localhost:5000/forms/admin/generate-from-source \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer <admin-token>' \
  -d '{
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
      { "canonicalKey": "MaBenhNhan",   "label": "Mã BN",      "fieldType": "Text" },
      { "canonicalKey": "TenBenhNhan",  "label": "Họ tên",     "fieldType": "Text" },
      { "canonicalKey": "NgaySinh",     "label": "Ngày sinh",  "fieldType": "Date",
        "displayFormat": "date:DD/MM/YYYY" },
      { "canonicalKey": "KhoaDieuTri",  "label": "Khoa",       "fieldType": "Text" },
      { "canonicalKey": "ChanDoan",     "label": "Chẩn đoán",  "fieldType": "Text" },
      { "canonicalKey": "NgayNhapVien", "label": "Ngày NV",    "fieldType": "Date",
        "displayFormat": "date:DD/MM/YYYY" }
    ]
  }'
```

Response 201 → screen `patient-detail` của module `datamatch` đã có trong DynamicForm DB.

### Bước 1.5 — Mở FE

```
http://localhost:5000/screen?module=datamatch&page=patient-detail&recordId=<rec-uuid>
```

FE tự:
1. Fetch `GET /forms/screens/datamatch/patient-detail/layout` → biết widgets + DataSource
2. Fetch `GET /dm/records/<rec-uuid>` → lấy `canonicalPayload`
3. Render `FormSectionWidget` với fields pre-fill từ data

→ **Không cần code FE thêm gì.**

---

## 4. Cách 2 — Lakehouse view (auto-enroll qua MVP B)

### Bước 2.1 — DE tạo view trong warehouse Postgres

DE (hoặc bạn để test) chuẩn bị warehouse + view + grant:

```bash
docker exec -i warehouse-postgres psql -U warehouse_admin -d warehouse <<'SQL'
CREATE SCHEMA IF NOT EXISTS warehouse;

CREATE TABLE IF NOT EXISTS warehouse.fact_lab_results (
    id            BIGSERIAL PRIMARY KEY,
    business_key  TEXT NOT NULL,
    hba1c         NUMERIC(4,1),
    blood_glucose NUMERIC(5,1),
    weight_kg     NUMERIC(5,2),
    height_m      NUMERIC(3,2),
    measured_at   TIMESTAMPTZ NOT NULL,
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO warehouse.fact_lab_results (business_key, hba1c, blood_glucose, weight_kg, height_m, measured_at)
VALUES
  ('BN2024001', 7.2, 142.5, 70.0, 1.65, NOW() - INTERVAL '7 days'),
  ('BN2024002', 5.8, 105.0, 65.0, 1.70, NOW() - INTERVAL '3 days'),
  ('BN2024003', 8.5, 180.0, 75.0, 1.72, NOW() - INTERVAL '1 day');

CREATE OR REPLACE VIEW warehouse.v_lab_results_v1 AS
SELECT business_key, hba1c, blood_glucose, weight_kg, height_m, measured_at, updated_at
FROM warehouse.fact_lab_results;

-- Quan trọng: cấp quyền cho user mà LakehouseService dùng
GRANT USAGE ON SCHEMA warehouse TO hdos_reader;
GRANT SELECT ON warehouse.v_lab_results_v1 TO hdos_reader;
SQL
```

### Bước 2.2 — Tạo ViewBinding với auto-enroll (1 call duy nhất)

MVP B endpoint — backend tự introspect view + sinh mapping + enroll SourceProfile + tạo binding:

```bash
curl -X POST http://localhost:5000/lakehouse/view-bindings/with-auto-profile \
  -H 'Content-Type: application/json' \
  -d '{
    "viewName":            "warehouse.v_lab_results_v1",
    "sourceSystem":        "lakehouse:v_lab_results_v1",
    "recordType":          "lab-result",
    "businessKeyColumn":   "business_key",
    "updatedAtColumn":     "updated_at",
    "pollIntervalSeconds": 300,
    "displayName":         "Lab Results — Warehouse v1"
  }'
```

Response 201:

```json
{
  "data": {
    "binding": {
      "id":       "binding-uuid",
      "viewName": "warehouse.v_lab_results_v1",
      "isActive": true,
      "..."
    },
    "profileEnrolled":  true,
    "businessKeyField": "MaBenhNhan",
    "mappings": {
      "business_key":  "MaBenhNhan",
      "hba1c":         "Hba1c",
      "blood_glucose": "BloodGlucose",
      "weight_kg":     "WeightKg",
      "height_m":      "HeightM",
      "measured_at":   "MeasuredAt",
      "updated_at":    "_updated_at"
    }
  }
}
```

Đằng sau, LakehouseService đã:
1. Query `information_schema.columns` → 7 columns
2. Sinh canonical name convention (`business_key` → `MaBenhNhan` vì có override domain healthcare)
3. Gọi `POST /dm/sources` ở DataMatching qua HTTP nội bộ — tạo SourceProfile
4. Lưu ViewBinding trong Lakehouse DB

> Mapping convention không hoàn hảo (vd `hba1c` → `Hba1c` thay vì `HbA1c`). Nếu muốn fine-tune, gọi `PUT /dm/sources/{id}` sau (chưa có UI) hoặc xài hướng C khi đã code (xem doc 45 §6).

### Bước 2.3 — Trigger sync ngay (không đợi worker)

```bash
curl -X POST http://localhost:5000/lakehouse/view-bindings/<binding-uuid>/sync
```

Response 202:

```json
{
  "data": {
    "bindingId": "binding-uuid",
    "viewName":  "warehouse.v_lab_results_v1",
    "rowCount":  3,
    "jobId":     "sync-lab-result-20260607...",
    "duration":  "00:00:01.234"
  }
}
```

→ 3 row trong view đã publish 3 event `RawRecordIngestRequestedIntegrationEvent` lên RabbitMQ. DataMatching consumer pick lên, apply mapping, dedup, lưu `StagingRecord`.

**Verify event đã chảy qua RabbitMQ:**

Mở `http://localhost:15672` (guest/guest) → Queues → tìm queue:
```
data-matching-service:raw-record-ingest-requested-integration-event
```

Số message tăng lên = backend đã publish thành công.

### Bước 2.4 — Verify ở DataMatching

```bash
# Đợi ~30s MatchingWorker xử lý
curl 'http://localhost:5000/dm/records?sourceSystem=lakehouse:v_lab_results_v1&recordType=lab-result'
```

Expected:

```json
{
  "data": [
    {
      "businessKey": "BN2024001",
      "status":      "Matched",
      "canonicalPayload": "{\"MaBenhNhan\":\"BN2024001\",\"Hba1c\":7.2,\"BloodGlucose\":142.5,\"WeightKg\":70.0,...}"
    },
    {
      "businessKey": "BN2024002",
      ...
    },
    {
      "businessKey": "BN2024003",
      ...
    }
  ]
}
```

### Bước 2.5 — Auto-gen DynForm screen

Y hệt Cách 1 Bước 1.4 nhưng đổi field theo canonical mới:

```bash
curl -X POST http://localhost:5000/forms/admin/generate-from-source \
  -H 'Content-Type: application/json' \
  -d '{
    "moduleCode": "lab",
    "screenCode": "lab-result-detail",
    "screenTitle":"Chi tiết xét nghiệm",
    "formKey":    "lab-result-form",
    "dataSource": {
      "namespace":      "record",
      "serviceId":      "datamatch",
      "resourcePath":   "/dm/records/{recordId}",
      "requiredParams": ["recordId"]
    },
    "fields": [
      { "canonicalKey": "MaBenhNhan",   "label": "Mã BN",        "fieldType": "Text"   },
      { "canonicalKey": "Hba1c",        "label": "HbA1c",        "fieldType": "Number" },
      { "canonicalKey": "BloodGlucose", "label": "Glucose",      "fieldType": "Number" },
      { "canonicalKey": "WeightKg",     "label": "Cân nặng (kg)","fieldType": "Number" },
      { "canonicalKey": "HeightM",      "label": "Chiều cao (m)","fieldType": "Number" }
    ]
  }'
```

> **Lưu ý**: `canonicalKey` phải khớp EXACT với key trong `canonicalPayload` (case-sensitive). Xem mappings response ở Bước 2.2 để biết tên chính xác.

### Bước 2.6 — Mở FE

```
http://localhost:5000/screen?module=lab&page=lab-result-detail&recordId=<rec-uuid>
```

→ Form pre-fill với canonical fields giống hệt Cách 1.

---

## 5. So sánh 2 cách

| Tiêu chí | Cách 1 (DataMatching push) | Cách 2 (Lakehouse view auto-enroll) |
|---|---|---|
| Khi nào dùng | Source tự push sang Hdos (HIS, BHYT, file) | Data đã sẵn trong warehouse Postgres (qua ETL DE) |
| Số API call admin gõ | 2 (`/dm/sources` + `/forms/admin/generate-from-source`) — ingest do external | 2 (`/lakehouse/.../with-auto-profile` + `/forms/.../generate-from-source`) — sync do worker |
| Bạn phải biết schema trước? | ✅ Có — gõ mappings tay | ❌ Không — backend introspect |
| Thời gian onboard | ~5 phút | ~3 phút |
| Realtime? | ✅ Có — push ngay khi có data | ⏸ Định kỳ — poll mỗi `pollIntervalSeconds` |
| Phù hợp với data type | Đơn lẻ, realtime, schema biết trước | Aggregated, time-series, schema rộng |
| Endpoint canonical FE | `/dm/records/{id}` | `/dm/records/{id}` (giống) |
| Code FE | ✅ Không cần đụng | ✅ Không cần đụng |
| Control mapping name | ✅ Đầy đủ (admin gõ) | ⚠ Convention auto, đổi qua `PUT /dm/sources/{id}` sau |

---

## 6. Pitfalls — lỗi hay gặp + fix

| Lỗi | Triệu chứng | Fix |
|---|---|---|
| Cách 1: Push record nhưng `/dm/records` rỗng | Status `Pending` mãi | Đợi 30s. Nếu vẫn vậy → `docker compose logs datamatchingservice` xem `MatchingWorker` |
| Cách 1: 404 NotFound khi push | SourceProfile chưa đăng ký | Quay lại Bước 1.1 |
| Cách 1: 409 Conflict khi push | Record giống hệt đã ingest trước (SHA-256 dedup) | Bình thường — payload duplicate bị skip idempotent. Đổi 1 field bất kỳ thì record mới |
| Cách 2: 502 BadGateway | DataMatching không reachable từ Lakehouse | Check env `Services__DataMatching__BaseUrl` trong docker-compose, restart `lakehouseservice` |
| Cách 2: 404 "View không tồn tại" | DE chưa GRANT SELECT cho `hdos_reader` | `GRANT SELECT ON warehouse.v_xxx_v1 TO hdos_reader;` |
| Cách 2: 400 "Cột không có trong view" | Typo `businessKeyColumn` / `updatedAtColumn` | Check tên cột bằng `\d warehouse.v_xxx_v1` trong psql |
| Cách 2: 409 Conflict tạo binding | Đã có binding cho view này | `DELETE /lakehouse/view-bindings/{id}` trước, hoặc dùng `PUT` nếu chỉ cần update |
| FE mở screen trắng | Quên auto-gen form | Chạy Bước 1.4 (Cách 1) hoặc Bước 2.5 (Cách 2) |
| FE pre-fill rỗng | `canonicalKey` ở form sai (case-sensitive) | Tên trong `canonicalPayload` mới đúng. VD Cách 2 sinh `Hba1c` thì form phải khai `Hba1c`, không phải `HbA1c` |
| FE error "DataSource not found" | DataSource trong layout response rỗng | Verify `GET /forms/screens/{m}/{s}/layout` có `dataSources` array với resourcePath đúng |

---

## 7. Thêm nguồn data thứ 3, 4, 5...

Lặp lại flow trên cho từng nguồn:

| Đơn vị | Tách theo |
|---|---|
| SourceProfile | 1 cặp `(sourceSystem, recordType)` |
| ViewBinding | 1 view lakehouse |
| DynForm Screen | 1 màn hình hiển thị |

**Không cần restart service, không cần deploy FE.** Add nguồn = HTTP call. Đẩy data = HTTP call hoặc tự động qua worker.

Ví dụ pipeline thực tế khi onboard nguồn thứ 4 (Excel xuất hàng tuần từ phòng tài chính):

```bash
# 1. Đăng ký SourceProfile
curl -X POST .../dm/sources -d '{ "sourceSystem":"finance-weekly", "recordType":"invoice", ... }'

# 2. Mỗi sáng thứ 2, admin upload file
curl -X POST .../dm/ingest/file \
  -F 'file=@invoices-2026-W23.csv' \
  -F 'sourceSystem=finance-weekly' \
  -F 'recordType=invoice'

# 3. Auto-gen screen (chỉ lần đầu)
curl -X POST .../forms/admin/generate-from-source -d '{ "moduleCode":"finance", ... }'

# 4. Bác sĩ tài chính mở /screen?module=finance&page=invoice-detail&recordId=...
```

---

## 8. Tóm lại — quy tắc vàng

> **Source → cách 1 (`POST /dm/ingest/json`) hoặc cách 2 (`POST /lakehouse/view-bindings/with-auto-profile` + `POST /.../sync`)**
> **→ DataMatching canonicalize → `/dm/records/{id}` → DynForm screen → FE auto render**

3 service riêng (DataMatching + Lakehouse + DynForm) **không biết về nhau ở FE level** — chúng chỉ liên kết qua:
- `(sourceSystem, recordType)` — định danh nguồn
- `recordId` — định danh 1 bản ghi
- DataSource `resourcePath` — chỉ trỏ DynForm tới endpoint DataMatching

Đó là điểm hay nhất của Unified Ingest Pipeline.

---

## Liên quan

- [22 — CDC với Debezium + Kafka](./22-cdc-debezium-kafka.md) — alternative realtime < 5s
- [23 — DataMatchingService](./23-data-matching-service.md) — core ingest engine
- [29 — DynamicFormService](./29-dynamic-form-service.md) — DataSource + screen
- [36 — DataMatch → DynForm Flow](./36-datamatch-to-dynform-flow.md) — chi tiết auto-gen form
- [43 — Warehouse Sync](./43-warehouse-sync-to-lakehouse.md) — pattern poll view
- [44 — Unified Ingest Pipeline](./44-unified-ingest-pipeline.md) — kiến trúc Phase 2
- [45 — Lakehouse Auto-Enroll SourceProfile](./45-lakehouse-auto-sourceprofile.md) — implementation MVP B (đã code) + hướng C (chưa)
