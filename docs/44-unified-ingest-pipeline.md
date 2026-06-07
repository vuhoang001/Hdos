# 44 — Unified Ingest Pipeline (Lakehouse → DataMatching → DynForm)

> **TL;DR.** Mọi nguồn dữ liệu (HIS, BHYT, file Excel, lakehouse view, API ngoài...) đều đi qua **một pipeline duy nhất**: publish `RawRecordIngestRequestedIntegrationEvent` → `DataMatchingService` apply `SourceProfile` mapping → lưu `StagingRecord` canonical → FE render qua DynForm `DataSource /dm/records/{id}` mà **không cần code FE mới**.

**Áp dụng cho:** Hệ thống Hdos hiện đang có 2 nguồn data (DataMatching + LakehouseSnapshot) và muốn hợp nhất thành 1 pipeline để: (1) thêm source mới = viết 1 connector, (2) FE rendering pipeline không cần biết source đến từ đâu.

**Replaces (về flow):**
- [Doc 39](./39-lakehouse-service.md) — `LakehouseSnapshot` storage + `/lakehouse/snapshots/*` endpoint (đánh dấu legacy)
- [Doc 43](./43-warehouse-sync-to-lakehouse.md) — `WarehouseViewSyncer` publish vào `LakehouseDataReadyIntegrationEvent` (đổi đích sang DataMatching)

**Không thay thế:**
- [Doc 23](./23-data-matching-service.md) — Core DataMatching: SourceProfile, MatchingWorker, dedup vẫn nguyên
- [Doc 36](./36-datamatch-to-dynform-flow.md) — Flow auto-generate DynForm từ source vẫn nguyên
- [Doc 22](./22-cdc-debezium-kafka.md) — CDC realtime, dùng cho < 5s latency

---

## Mục lục

1. [Vấn đề và mục tiêu](#1-vấn-đề-và-mục-tiêu)
2. [Kiến trúc đích](#2-kiến-trúc-đích)
3. [Integration Event mới](#3-integration-event-mới)
4. [ViewBinding — registry view ↔ SourceProfile](#4-viewbinding--registry-view--sourceprofile)
5. [Phân chia trách nhiệm 3 service](#5-phân-chia-trách-nhiệm-3-service)
6. [End-to-end flow](#6-end-to-end-flow)
7. [Cách thêm source mới](#7-cách-thêm-source-mới)
8. [Migration từ Phase 1 hiện tại](#8-migration-từ-phase-1-hiện-tại)
9. [Checklist setup](#9-checklist-setup)
10. [Troubleshooting](#10-troubleshooting)

---

## 1. Vấn đề và mục tiêu

### 1.1 Hiện trạng — 2 pipeline song song

```
┌─────────────────────────────────────────────────────────────────┐
│ Pipeline A — DataMatching                                       │
│                                                                 │
│  HIS, BHYT (REST/file)                                          │
│    └─► POST /dm/ingest/json                                     │
│         └─► SourceProfile mapping → canonical                   │
│              └─► StagingRecord + MatchingWorker                 │
│                   └─► GET /dm/records/{id} ─┐                   │
│                                              │                  │
└──────────────────────────────────────────────┼──────────────────┘
                                               │
┌──────────────────────────────────────────────┼──────────────────┐
│ Pipeline B — Lakehouse Snapshot              │                  │
│                                              │                  │
│  Warehouse Postgres VIEW                     │                  │
│    └─► WarehouseViewSyncer (poll 5m)         │                  │
│         └─► LakehouseDataReadyIntegrationEvent                  │
│              └─► LakehouseSnapshot (JSONB)                      │
│                   └─► GET /lakehouse/snapshots/latest ─┐        │
│                                                         │       │
└─────────────────────────────────────────────────────────┼───────┘
                                                          │
                                  ┌───────────────────────┴───────┐
                                  │ DynForm DataSource            │
                                  │  • /dm/records/{id}           │
                                  │  • /lakehouse/snapshots/latest│
                                  │                               │
                                  │ FE useDataSources hook        │
                                  │  • có if-branch unwrap        │
                                  │    canonicalPayload (DM only) │
                                  │  • có config phân biệt service│
                                  └───────────────────────────────┘
```

**Hệ quả tiêu cực:**

| Vấn đề | Ảnh hưởng |
|---|---|
| 2 entity store (`StagingRecord` vs `LakehouseSnapshot`) | Logic dedup, audit, query khác nhau — duplicate code |
| 2 REST endpoint (`/dm/records` vs `/lakehouse/snapshots`) | Admin phải biết source nào dùng endpoint nào |
| FE `useDataSources` có nhánh đặc biệt `unwrap canonicalPayload` | Logic FE phụ thuộc cấu trúc response từng service |
| Lakehouse data **không** đi qua SourceProfile mapping | Field name từ DB view phải khớp sẵn với FE binding, không thể rename |
| Thêm source mới = chọn pipeline → code khác nhau | Cost cao, dễ sai pattern |

### 1.2 Mục tiêu

```
1 pipeline duy nhất:
  Bất kỳ source nào → publish RawRecordIngestRequestedIntegrationEvent
                      → DataMatchingService consumer
                      → SourceProfile mapping (raw → canonical)
                      → StagingRecord
                      → GET /dm/records/{id}
                      → DynForm DataSource (đã có)
                      → FE render (không cần đổi)
```

**Quy tắc thiết kế:**

| Quy tắc | Lý do |
|---|---|
| **Một event contract duy nhất cho ingest** | Source mới chỉ cần publish event, không cần biết DataMatching internals |
| **DataMatching là điểm canonical hóa duy nhất** | Mỗi source khai báo `SourceProfile` → field mapping rõ ràng, đổi tên dễ |
| **FE chỉ biết 1 endpoint `/dm/records/{id}`** | Bỏ if-branch trong `useDataSources` |
| **LakehouseService giảm vai trò xuống "Source Provider"** | Không lưu data, chỉ pull view + publish event |

---

## 2. Kiến trúc đích

```
┌───────────────────────────────────────────────────────────────────────┐
│ EXTERNAL                                                              │
│                                                                       │
│  ┌────────────────┐    ┌──────────────────┐    ┌──────────────────┐   │
│  │ HIS / BHYT     │    │ Warehouse PG     │    │ Future API       │   │
│  │ (push REST)    │    │ VIEW v_xxx_v1    │    │ (push REST/file) │   │
│  └────────┬───────┘    └────────┬─────────┘    └────────┬─────────┘   │
│           │                     │                       │             │
└───────────│─────────────────────│───────────────────────│─────────────┘
            │                     │                       │
┌───────────│─────────────────────│───────────────────────│─────────────┐
│ HDOS      │                     │                       │             │
│           │                     │  poll Npgsql          │             │
│           │            ┌────────▼─────────┐             │             │
│           │            │ LakehouseService │             │             │
│           │            │  • ViewBinding   │             │             │
│           │            │    registry      │             │             │
│           │            │  • Poller worker │             │             │
│           │            └────────┬─────────┘             │             │
│           │                     │                       │             │
│           │  POST /dm/ingest    │ publish event         │ POST event  │
│           │                     │                       │             │
│           ▼                     ▼                       ▼             │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │ RabbitMQ — RawRecordIngestRequestedIntegrationEvent          │    │
│  └──────────────────────────────┬───────────────────────────────┘    │
│                                 │ consume                            │
│                                 ▼                                     │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │ DataMatchingService                                          │    │
│  │  • RawRecordIngestRequestedConsumer                          │    │
│  │  • IIngestCoreService (shared với /dm/ingest/json)           │    │
│  │     - Lookup SourceProfile by (sourceSystem, recordType)     │    │
│  │     - ApplyMappings: raw JSON → canonical JSON               │    │
│  │     - SHA-256 dedup                                          │    │
│  │     - Save StagingRecord                                     │    │
│  │  • MatchingWorker (≤ 30s, không đổi)                         │    │
│  │  • GET /dm/records/{id} (không đổi)                          │    │
│  └──────────────────────────────┬───────────────────────────────┘    │
│                                 │                                     │
│                                 ▼                                     │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │ DynamicFormService — Screen DataSource (không đổi)           │    │
│  │   { namespace: "record", resourcePath: "/dm/records/{id}" }  │    │
│  └──────────────────────────────┬───────────────────────────────┘    │
│                                 │                                     │
│                                 ▼                                     │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │ FE — useDataSources (không đổi, unwrap canonicalPayload)    │    │
│  │      → FormSectionWidget render                              │    │
│  └──────────────────────────────────────────────────────────────┘    │
└───────────────────────────────────────────────────────────────────────┘
```

**Điểm đáng chú ý:**

- **3 ô màu vàng (DataMatching, DynForm, FE) hoàn toàn không cần đổi** flow — chỉ thêm 1 consumer mới ở DataMatching.
- LakehouseService **không còn** `LakehouseSnapshot` storage, **không còn** `/lakehouse/snapshots/*` endpoint (xem migration ở [section 8](#8-migration-từ-phase-1-hiện-tại)).
- Source mới nhập cuộc: chỉ cần **publish event** + đăng ký `SourceProfile`. Không phải đăng ký gì ở LakehouseService nếu không phải lakehouse view.

---

## 3. Integration Event mới

### 3.1 Định nghĩa

File: `src/BuildingBlocks/Contracts/IntegrationEvents/RawRecordIngestRequestedIntegrationEvent.cs`

```csharp
namespace Hdos.Contracts.IntegrationEvents;

public sealed record RawRecordIngestRequestedIntegrationEvent(
    string  SourceSystem,    // "lakehouse:v_lab_results_v1", "his-01", "bhyt-hn"
    string  RecordType,      // "lab-result", "benh-nhan", "chung-tu"
    string  BusinessKey,     // "BN-2024-001"
    string  RawPayloadJson,  // JSON string của 1 record raw (chưa qua mapping)
    string? SourceJobId)     // optional — id của batch/poll job
    : IntegrationEvent;
```

`IntegrationEvent` base đã cung cấp `EventId`, `OccurredOnUtc`, `CorrelationId`, `CausationId`, `Source`, `Version` (xem `Contracts/IntegrationEvents/IntegrationEvent.cs`) — không cần re-declare. `CorrelationId` auto-set từ `Activity.Current.TraceId` bởi `MassTransitEventBus`.

### 3.2 Tại sao chọn các field này

| Field | Vì sao bắt buộc / optional |
|---|---|
| `SourceSystem` | Khóa thứ 1 tra `SourceProfile`. Convention: nguồn lakehouse dùng tiền tố `lakehouse:` để phân biệt với HIS/BHYT |
| `RecordType` | Khóa thứ 2 tra `SourceProfile`. Cùng `sourceSystem` có thể có nhiều record type |
| `BusinessKey` | Cho phép producer set sẵn, không phải parse từ payload. Nếu rỗng, consumer fallback dùng `SourceProfile.BusinessKeyField` để trích từ canonical payload |
| `RawPayloadJson` | Payload JSON 1 record duy nhất (không phải batch). Producer nào có batch phải fan-out thành N event |
| `SourceJobId` | optional — nullable. Lakehouse poller set `sync-{recordType}-{timestamp}` để truy vết batch |

### 3.3 Vì sao không reuse `LakehouseDataReadyIntegrationEvent`

| Vấn đề của event cũ | Vì sao không phù hợp |
|---|---|
| Có cả `Payload` và `DownloadUrl` (1 trong 2) | Phức tạp, DataMatching consumer không cần xử lý download |
| `Namespace` thay vì `(SourceSystem, RecordType)` | DataMatching lookup `SourceProfile` cần đúng 2 trường này |
| Cho phép `Payload` là JSON array `[...]` (batch) | DataMatching dedup theo SHA-256 từng record, batch khó debug |
| Tên semantic "lakehouse" | Event mới phải dùng được cho mọi source |

Event cũ `LakehouseDataReadyIntegrationEvent` **được giữ trong code** (vì doc 39 còn reference) nhưng đánh dấu obsolete, không có consumer mới đăng ký.

---

## 4. ViewBinding — registry view ↔ SourceProfile

LakehouseService cần biết: với mỗi PG view, **gán** nó tương ứng `SourceProfile` nào trong DataMatching. Đây là việc của admin (không hardcode).

### 4.1 Entity

File: `src/Services/LakehouseService/LakehouseService.Domain/Entities/ViewBinding.cs`

```csharp
public sealed class ViewBinding : AggregateRoot<Guid>
{
    public string  ViewName            { get; private set; } = null!;  // "warehouse.v_lab_results_v1"
    public string  SourceSystem        { get; private set; } = null!;  // "lakehouse:v_lab_results_v1"
    public string  RecordType          { get; private set; } = null!;  // "lab-result"
    public string  BusinessKeyColumn   { get; private set; } = null!;  // "business_key"
    public string  UpdatedAtColumn     { get; private set; } = null!;  // "updated_at" (cho incremental poll)
    public int     PollIntervalSeconds { get; private set; }           // 300 (5 phút)
    public bool    IsActive            { get; private set; }
    public DateTime CreatedAtUtc       { get; private set; }
    public DateTime? UpdatedAtUtc      { get; private set; }

    public static ViewBinding Create(
        string viewName,
        string sourceSystem,
        string recordType,
        string businessKeyColumn,
        string updatedAtColumn,
        int pollIntervalSeconds)
    { /* ... */ }
}
```

### 4.2 REST API (LakehouseService)

| Method | Endpoint | Mô tả |
|---|---|---|
| `GET`    | `/lakehouse/view-bindings`           | List tất cả |
| `POST`   | `/lakehouse/view-bindings`           | Create binding mới |
| `PUT`    | `/lakehouse/view-bindings/{id}`      | Update |
| `DELETE` | `/lakehouse/view-bindings/{id}`      | Soft delete (set IsActive=false) |
| `POST`   | `/lakehouse/view-bindings/{id}/sync` | Trigger 1 lần sync thủ công |

### 4.3 Quy trình admin gắn 1 source lakehouse mới

```
[1] DE: tạo VIEW v_xxx_v1 trong warehouse Postgres (xem doc 43)
[2] DE: cấp GRANT SELECT cho hdos_reader

[3] Admin Hdos: POST /dm/sources — đăng ký SourceProfile
    {
      "sourceSystem":      "lakehouse:v_lab_results_v1",
      "recordType":        "lab-result",
      "displayName":       "Lab Results — Warehouse v1",
      "businessKeyField":  "MaBenhNhan",
      "mappings": {
        "business_key":         "MaBenhNhan",
        "hba1c":                "HbA1c",
        "blood_glucose":        "Glucose",
        "bmi":                  "BMI",
        "avg_hba1c_30d":        "HbA1cTrungBinh30Ngay",
        "last_measured_at":     "NgayDoGanNhat"
      }
    }

[4] Admin Hdos: POST /lakehouse/view-bindings — đăng ký ViewBinding
    {
      "viewName":            "warehouse.v_lab_results_v1",
      "sourceSystem":        "lakehouse:v_lab_results_v1",
      "recordType":          "lab-result",
      "businessKeyColumn":   "business_key",
      "updatedAtColumn":     "updated_at",
      "pollIntervalSeconds": 300
    }

[5] WarehouseViewSyncer worker tự động pick up:
    - Đọc binding active
    - Mỗi 5 phút, SELECT WHERE updated_at > lastSync FROM warehouse.v_lab_results_v1
    - Mỗi row → publish RawRecordIngestRequestedIntegrationEvent

[6] DataMatching consume → SourceProfile mapping → StagingRecord
    Sau ≤ 30s, GET /dm/records?sourceSystem=lakehouse:v_lab_results_v1
    đã thấy record với canonical fields HbA1c, Glucose, BMI...

[7] DynForm screen cấu hình DataSource /dm/records/{id} → FE render
    (xem doc 36 — Full flow, không đổi)
```

---

## 5. Phân chia trách nhiệm 3 service

| Service | Vai trò trong pipeline |
|---|---|
| **LakehouseService** | **Source Provider**. Đọc PG view (qua Npgsql), publish event. **Không** lưu data record. |
| **DataMatchingService** | **Unified Ingest Hub**. Consume event, lookup `SourceProfile`, canonicalize, dedup, lưu `StagingRecord`. Endpoint `/dm/records/*` là điểm exit duy nhất cho FE. |
| **DynamicFormService** | **Screen Mapper**. Cấu hình `DataSource` trỏ `/dm/records/{id}`. Hoàn toàn agnostic về source. |

**LakehouseService giờ KHÔNG còn:**
- `LakehouseSnapshot` entity (xoá khỏi domain)
- `/lakehouse/snapshots/*` endpoints (xoá khỏi API)
- `LakehouseDataReadyConsumer` (xoá khỏi infrastructure)

**LakehouseService giữ:**
- `WarehouseViewSyncer` + `WarehousePollerWorker` (sửa: publish event mới)
- `SyncState` (theo dõi `last_synced_at` từng binding)
- `LakehouseDbContext` (giữ cho `SyncState` + `ViewBinding`)

---

## 6. End-to-end flow

> Toàn bộ chạy trên local. Dataset minh hoạ: lab results bệnh nhân.

### 6.1 Setup warehouse mock

```bash
docker run -d --name warehouse-postgres --network hdos-net \
  -e POSTGRES_DB=warehouse -e POSTGRES_USER=warehouse_admin \
  -e POSTGRES_PASSWORD=warehouse_pass -p 5436:5432 postgres:16-alpine

# Áp dụng schema + seed + VIEW (file SQL ở doc 43 mục 3.2 — không đổi)
docker exec -i warehouse-postgres psql -U warehouse_admin -d warehouse < sql/warehouse/001_schema.sql
docker exec -i warehouse-postgres psql -U warehouse_admin -d warehouse < sql/warehouse/002_seed_data.sql
docker exec -i warehouse-postgres psql -U warehouse_admin -d warehouse < sql/warehouse/003_view_v1.sql
docker exec -i warehouse-postgres psql -U warehouse_admin -d warehouse < sql/warehouse/999_reader_role.sql
```

### 6.2 Đăng ký SourceProfile (DataMatching)

```http
POST http://localhost:5000/dm/sources
Content-Type: application/json

{
  "sourceSystem":     "lakehouse:v_lab_results_v1",
  "recordType":       "lab-result",
  "displayName":      "Lab Results — Warehouse v1",
  "businessKeyField": "MaBenhNhan",
  "mappings": {
    "business_key":          "MaBenhNhan",
    "hba1c":                 "HbA1c",
    "blood_glucose":         "Glucose",
    "bmi":                   "BMI",
    "avg_hba1c_30d":         "HbA1cTrungBinh30Ngay",
    "measurement_count_30d": "SoLanDo30Ngay",
    "last_measured_at":      "NgayDoGanNhat"
  }
}
```

### 6.3 Đăng ký ViewBinding (LakehouseService)

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
```

### 6.4 Sync chạy (5 phút sau, hoặc trigger thủ công)

```http
POST http://localhost:5000/lakehouse/view-bindings/{id}/sync

# → Backend log:
# Warehouse sync v_lab_results_v1: 1000 records, lastSync 0001-01-01 → 2026-06-05 10:30
```

Behind the scenes, mỗi row trở thành 1 event (đã bao gồm các field kế thừa từ `IntegrationEvent` base):

```json
{
  "eventId":       "9a1d...",
  "eventType":     "RawRecordIngestRequestedIntegrationEvent",
  "source":        "LakehouseService",
  "occurredOnUtc": "2026-06-05T10:30:00Z",
  "correlationId": "00-abcd...-1234-01",
  "version":       "1.0",
  "sourceSystem":  "lakehouse:v_lab_results_v1",
  "recordType":    "lab-result",
  "businessKey":   "BN-0001",
  "rawPayloadJson":"{\"business_key\":\"BN-0001\",\"hba1c\":7.2,\"blood_glucose\":142.3,\"bmi\":26.5,...}",
  "sourceJobId":   "sync-lab-result-20260605103000"
}
```

DataMatching consumer xử lý: apply mapping → canonical `{ "MaBenhNhan": "BN-0001", "HbA1c": 7.2, ... }` → save `StagingRecord`.

### 6.5 Verify

```http
GET http://localhost:5000/dm/records?sourceSystem=lakehouse:v_lab_results_v1&recordType=lab-result
GET http://localhost:5000/dm/records/{recordId}

# Response:
# {
#   "data": {
#     "id":           "...",
#     "sourceSystem": "lakehouse:v_lab_results_v1",
#     "recordType":   "lab-result",
#     "businessKey":  "BN-0001",
#     "canonicalPayload": {
#       "MaBenhNhan":             "BN-0001",
#       "HbA1c":                  7.2,
#       "Glucose":                142.3,
#       "BMI":                    26.5,
#       "HbA1cTrungBinh30Ngay":   7.1,
#       "SoLanDo30Ngay":          3,
#       "NgayDoGanNhat":          "2026-06-04T10:30:00Z"
#     },
#     "status": "Matched"
#   }
# }
```

### 6.6 Render qua DynForm (không có code mới phía FE)

```http
POST http://localhost:5000/forms/admin/generate-from-source
Content-Type: application/json

{
  "moduleCode":  "lab",
  "screenCode":  "lab-result-detail",
  "screenTitle": "Chi tiết xét nghiệm",
  "formKey":     "lab-result-form",
  "dataSource": {
    "namespace":      "record",
    "serviceId":      "datamatch",
    "resourcePath":   "/dm/records/{recordId}",
    "requiredParams": ["recordId"]
  },
  "fields": [
    { "canonicalKey": "MaBenhNhan",           "label": "Mã bệnh nhân", "fieldType": "Text" },
    { "canonicalKey": "HbA1c",                "label": "HbA1c",        "fieldType": "Number" },
    { "canonicalKey": "Glucose",              "label": "Glucose",      "fieldType": "Number" },
    { "canonicalKey": "BMI",                  "label": "BMI",          "fieldType": "Number" },
    { "canonicalKey": "HbA1cTrungBinh30Ngay", "label": "HbA1c TB 30 ngày", "fieldType": "Number" },
    { "canonicalKey": "NgayDoGanNhat",        "label": "Ngày đo gần nhất", "fieldType": "Date", "displayFormat": "date:DD/MM/YYYY" }
  ]
}
```

FE mở `/lab/lab-result-detail/{recordId}` → `useDataSources` fetch `/dm/records/{recordId}` → form pre-fill ngay. **Code FE đã có sẵn từ doc 36, không sửa gì.**

---

## 7. Cách thêm source mới

### 7.1 Source là lakehouse view khác (case phổ biến nhất)

```
[1] DE tạo VIEW mới — xem doc 43 mục 4.2 (versioning)
[2] POST /dm/sources         — khai báo SourceProfile (mapping field)
[3] POST /lakehouse/view-bindings — đăng ký view
[4] WarehousePollerWorker tự pick up → record xuất hiện ở /dm/records
[5] POST /forms/admin/generate-from-source — auto-gen screen (xem doc 36)
```

**Không có dòng code .NET hay TypeScript nào cần viết.**

### 7.2 Source là API ngoài push vào (HIS, BHYT)

```
[1] POST /dm/sources         — khai báo SourceProfile
[2] External system gọi POST /dm/ingest/json mỗi khi có record mới
   (HOẶC nếu external có thể publish RabbitMQ → publish event mới)
[3] Tương tự bước [5] phía trên
```

### 7.3 Source mới hoàn toàn (Salesforce, Google Sheet, ...)

Tạo 1 service `XxxSourceService` mới với pattern giống LakehouseService:

```
src/Services/XxxSourceService/
├── XxxSourceService.Domain/
│   ├── Entities/XxxBinding.cs       — config mapping
│   └── ...
├── XxxSourceService.Application/
│   └── Features/                    — CRUD binding
├── XxxSourceService.Infrastructure/
│   ├── Sync/XxxSyncer.cs            — pull từ Xxx API
│   ├── Sync/XxxPollerWorker.cs      — BackgroundService
│   └── Persistence/                 — DbContext riêng
└── XxxSourceService.API/
    └── Controllers/                 — REST CRUD
```

`XxxSyncer` publish `RawRecordIngestRequestedIntegrationEvent`. Hết. DataMatching không cần biết source mới.

### 7.4 Source có data lớn (file Excel hàng triệu row)

Vẫn dùng event, nhưng publish theo batch nhỏ:

```csharp
// Trong worker / handler đọc file:
foreach (var row in rows.Chunk(500))
{
    foreach (var r in row)
        await eventBus.PublishAsync(new RawRecordIngestRequestedIntegrationEvent(
            SourceSystem:   "excel:lab-import",
            RecordType:     "lab-result",
            BusinessKey:    r["business_key"],
            RawPayloadJson: JsonSerializer.Serialize(r),
            SourceJobId:    jobId,
            CorrelationId:  Activity.Current?.Id,
            OccurredAtUtc:  DateTime.UtcNow), ct);

    await Task.Delay(100, ct);  // throttle để DataMatching không bị flood
}
```

---

## 8. Migration từ Phase 1 hiện tại

### 8.1 Lộ trình 3 bước (zero-downtime)

```
┌──────────────────────────────────────────────────────────────┐
│ Bước 1 — Coexist (compatibility window ~1 tuần)              │
│                                                              │
│  • Thêm RawRecordIngestRequestedIntegrationEvent (Contracts) │
│  • Thêm RawRecordIngestRequestedConsumer ở DataMatching      │
│  • Lakehouse vẫn publish CẢ HAI event (cũ + mới)             │
│  • LakehouseSnapshot vẫn lưu, /lakehouse/snapshots vẫn chạy  │
│  • Mọi DynForm screen mới: dùng /dm/records/{id}             │
│                                                              │
│  Verify: data nào ở /lakehouse/snapshots cũng có ở /dm/records│
└──────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────┐
│ Bước 2 — Migrate FE screens                                  │
│                                                              │
│  • Sửa DynForm screen cũ trỏ /lakehouse/snapshots/latest     │
│    → đổi sang /dm/records/{id}                               │
│  • Update binding expression {{sources.labResults.X}}        │
│    → {{sources.record.X}}                                    │
│  • Verify từng screen 1 — đợi 24h không có bug               │
└──────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────┐
│ Bước 3 — Deprecate                                           │
│                                                              │
│  • Tắt code publish LakehouseDataReadyIntegrationEvent ở     │
│    WarehouseViewSyncer                                       │
│  • Xoá LakehouseDataReadyConsumer khỏi LakehouseService      │
│  • Xoá SnapshotsController + LakehouseSnapshotRepository     │
│  • Migration: drop bảng LakehouseSnapshots                   │
│  • Giữ ViewBinding + SyncState                               │
└──────────────────────────────────────────────────────────────┘
```

> **Lưu ý:** Vì hệ thống đang ở giai đoạn dev (không có data production), có thể skip Bước 1+2, đi thẳng Bước 3 — nhưng giữ migration script để rollback.

### 8.2 Bảng đối chiếu file

| Phase 1 (hiện tại) | Phase 2 (mới) | Action |
|---|---|---|
| `LakehouseDataReadyIntegrationEvent` | — | Giữ trong Contracts (obsolete attribute), không có producer/consumer mới |
| — | `RawRecordIngestRequestedIntegrationEvent` | **TẠO MỚI** ở `Contracts/IntegrationEvents/` |
| `Lakehouse.Infrastructure/Consumers/LakehouseDataReadyConsumer.cs` | — | **XOÁ** ở Bước 3 |
| `Lakehouse.Domain/Entities/LakehouseSnapshot.cs` | — | **XOÁ** ở Bước 3 |
| `Lakehouse.API/Controllers/SnapshotsController.cs` | — | **XOÁ** ở Bước 3 |
| `Lakehouse.Infra/Sync/WarehouseViewSyncer.cs` | sửa publish event mới | **SỬA** |
| — | `Lakehouse.Domain/Entities/ViewBinding.cs` | **TẠO MỚI** |
| — | `Lakehouse.API/Controllers/ViewBindingsController.cs` | **TẠO MỚI** |
| — | `DataMatching.Application/Services/IIngestCoreService.cs` | **TẠO MỚI** (refactor từ `IngestJsonHandler`) |
| — | `DataMatching.Infra/Messaging/Consumers/RawRecordIngestRequestedConsumer.cs` | **TẠO MỚI** |

### 8.3 Sau migration, doc nào còn đúng?

| Doc | Còn đúng? | Ghi chú |
|---|---|---|
| 23 — DataMatchingService | ✅ Hoàn toàn | Bổ sung 1 dòng "consume RawRecordIngestRequested" ở phần luồng vào |
| 24 — Dashboard DataMatching | ✅ Hoàn toàn | Dashboard engine không bị ảnh hưởng |
| 25 — SDUI | ✅ Hoàn toàn | SDUI engine không phụ thuộc source |
| 29 — DynamicFormService | ✅ Hoàn toàn | DataSource API generic, không phân biệt source |
| 36 — DataMatch → DynForm | ✅ Hoàn toàn | Mở rộng: thêm 1 mục "Lakehouse as source" (cập nhật ở doc 36) |
| 39 — LakehouseService | ⚠️ Phần Phase 1 (SnapshotsController) đánh dấu deprecated | Cập nhật ở doc 39 |
| 43 — Warehouse Sync | ⚠️ Sửa destination publish event | Cập nhật ở doc 43 |

---

## 9. Checklist setup

### 9.1 Backend checklist

```
[ ] Thêm RawRecordIngestRequestedIntegrationEvent vào BuildingBlocks/Contracts
[ ] Refactor DataMatching IngestJsonHandler → tách IIngestCoreService
[ ] Tạo RawRecordIngestRequestedConsumer ở DataMatching.Infrastructure
[ ] Register consumer trong DataMatching.Infrastructure DependencyInjection
[ ] Tạo ViewBinding entity + repository + EF config + migration
[ ] CRUD endpoints ViewBindingsController + Application Features
[ ] Sửa WarehouseViewSyncer:
    [ ] Đọc bindings từ ViewBindingRepository (thay constants)
    [ ] Foreach binding active: build SQL từ view name + columns
    [ ] Publish RawRecordIngestRequestedIntegrationEvent (thay event cũ)
[ ] Healthcheck: query smoke test 1 row mỗi view binding active
[ ] dotnet build pass
[ ] dotnet test pass (xUnit cho DataMatching + LakehouseService)
```

### 9.2 Frontend checklist

```
[ ] adminApi: thêm listViewBindings, createViewBinding, updateViewBinding,
    deleteViewBinding, triggerSync
[ ] Tạo page /admin/lakehouse-views với:
    [ ] Table list + filter
    [ ] Form create/edit (viewName, sourceSystem, recordType, businessKeyColumn,
        updatedAtColumn, pollIntervalSeconds)
    [ ] Dropdown sourceSystem/recordType — lấy từ SourceProfile list
    [ ] Button "Sync now" trigger /sync
[ ] Test: chọn SourceProfile có sẵn → tạo binding → đợi 5 phút → 
    record xuất hiện ở /admin/datamatch/records
[ ] npm run type-check pass
[ ] npm run lint pass
```

### 9.3 Documentation checklist

```
[x] Tạo doc 44 (file này)
[ ] Update doc 39 — đánh dấu LakehouseSnapshot path là legacy, pointer sang 44
[ ] Update doc 43 — đổi destination publish event
[ ] Update doc 36 — thêm mục "Lakehouse view as source"
[ ] Update README.md — thêm dòng cho doc 44, cập nhật mô tả 39 + 43
```

---

## 10. Troubleshooting

### Lỗi 1: Record không xuất hiện ở `/dm/records` sau sync

**Check theo thứ tự:**

```
[1] LakehouseService log có "Warehouse sync ... N records" không?
    → Không: WarehouseViewSyncer không chạy. Check binding IsActive=true, worker startup.
    → Có: tiếp bước 2

[2] RabbitMQ UI (http://localhost:15672) — queue
    "DataMatchingService:RawRecordIngestRequestedIntegrationEvent" có message không?
    → Không: LakehouseService chưa publish được. Check log error.
    → Có message nhưng không consume: DataMatching service down hoặc consumer chưa register.
    → Consume nhưng error: tiếp bước 3

[3] DataMatching log — search "RawRecordIngestRequested"
    Thường gặp:
    - "SourceProfile 'xxx/yyy' not found" → đăng ký thiếu SourceProfile
    - "Duplicate payload" → dedup hit, OK (record đã ingest trước rồi)
    - JSON parse error → rawPayloadJson không hợp lệ, debug ở producer
```

### Lỗi 2: Field `canonicalPayload` thiếu field mong đợi

**Nguyên nhân:** `SourceProfile.mappings` không có entry cho field đó.

```
Raw payload: { "hba1c": 7.2, "abc": "xyz" }
Mappings:    { "hba1c": "HbA1c" }
Canonical:   { "HbA1c": 7.2, "abc": "xyz" }  ← "abc" giữ nguyên vì không có mapping
```

**Fix:** Thêm vào mappings, hoặc accept giữ nguyên (FE binding `{{sources.record.abc}}` vẫn hoạt động).

### Lỗi 3: Cùng record publish nhiều lần

Đây là **expected behavior** — view có thể update `updated_at` nhiều lần cho cùng `business_key`. DataMatching dedup SHA-256:

- Nếu raw payload giống hệt → `Error.Conflict("Duplicate payload")` → consumer ack message, không retry, không lưu record mới.
- Nếu raw payload có ít nhất 1 field khác → record mới được tạo (versioning by hash).

**Quy tắc:** consumer phải **ack** message khi hit dedup, không phải nack — vì nack sẽ retry infinitely.

### Lỗi 4: Sync chậm, queue RabbitMQ tăng cao

Đo P95 sync duration ở Prometheus:

```
histogram_quantile(0.95, rate(lakehouse_warehouse_sync_duration_seconds_bucket[5m]))
```

Nếu > 30s → giảm `BatchSize` xuống còn 500 hoặc tăng `PollIntervalSeconds`. Throttle publish bằng `Task.Delay(50)` giữa các event.

---

## Liên quan

- [22 — CDC Debezium + Kafka](./22-cdc-debezium-kafka.md) — Alternative cho realtime < 5s
- [23 — DataMatchingService](./23-data-matching-service.md) — Core ingest engine
- [29 — DynamicFormService](./29-dynamic-form-service.md) — DataSource + screen mapping
- [35 — Expression Data Binding](./35-expression-data-binding.md) — `{{sources.namespace.field}}` resolver
- [36 — DataMatch → DynForm](./36-datamatch-to-dynform-flow.md) — End-to-end auto-generate form
- [39 — LakehouseService](./39-lakehouse-service.md) — Phase 1 (legacy snapshot path)
- [43 — Warehouse Sync](./43-warehouse-sync-to-lakehouse.md) — Phase 2 (pattern poller)
