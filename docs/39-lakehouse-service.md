# 39 — LakehouseService

> ⚠️ **STATUS — Phase 1 (LEGACY).**
> Phase này lưu data lakehouse vào bảng `LakehouseSnapshots` riêng và expose qua `/lakehouse/snapshots/*`. Hệ thống đã chuyển sang **Unified Ingest Pipeline** (xem [doc 44](./44-unified-ingest-pipeline.md)): lakehouse data giờ chảy vào `DataMatchingService` qua event mới `RawRecordIngestRequestedIntegrationEvent` và truy cập qua `/dm/records/{id}` thống nhất với mọi source khác.
>
> **Tài liệu này giữ để:**
> - Hiểu lý do thiết kế ban đầu (RabbitMQ one-way → cần snapshot)
> - Tham chiếu code cũ trong thời gian migration
> - Khi nào nên dùng pattern snapshot riêng (xem section "Khi nào vẫn nên dùng Phase 1" ở cuối)
>
> **Vai trò mới của LakehouseService (Phase 2):** Source Provider — đọc PG view (Npgsql), publish event vào DataMatching. Không lưu snapshot. Xem doc 44 mục 2 + 4.

---

## Tổng quan

`LakehouseService` (Phase 1) là service trung gian nhận dữ liệu đã xử lý từ Lakehouse (qua RabbitMQ), lưu vào PostgreSQL dưới dạng snapshot JSONB, và expose REST API để DynamicFormService cùng FE query.

**Vấn đề giải quyết:** RabbitMQ là one-way push — FE không thể query ngược lại lakehouse theo yêu cầu. LakehouseService lưu snapshot để FE luôn có data đọc bất kỳ lúc nào.

> ℹ️ Trong Phase 2, vấn đề "FE query bất kỳ lúc nào" được giải quyết bằng `StagingRecord` ở DataMatching (xem doc 23) — không cần snapshot riêng.

---

## Kiến trúc luồng dữ liệu

```
Lakehouse (external)
        │ publish LakehouseDataReadyIntegrationEvent
        ▼
    RabbitMQ
        │ consume
        ▼
LakehouseService
  ├── Nếu event.Payload != null   → lưu thẳng
  └── Nếu event.DownloadUrl != null → download qua HTTP → lưu
        │ lưu vào LakehouseSnapshots (PostgreSQL + JSONB)
        ▼
REST API  GET /lakehouse/snapshots/latest?namespace=...&key=...
REST API  GET /lakehouse/snapshots?namespace=...&limit=...
        │
        ▼
FE / DynamicFormService DataSource
```

---

## Integration Event

### `LakehouseDataReadyIntegrationEvent`

Định nghĩa tại: `src/BuildingBlocks/Contracts/IntegrationEvents/LakehouseDataReadyIntegrationEvent.cs`

| Field | Type | Mô tả |
|-------|------|-------|
| `JobId` | string | ID của job xử lý trong lakehouse |
| `Namespace` | string | Nhóm dữ liệu (VD: `"lab-results"`, `"vital-signs"`) |
| `BusinessKey` | string | Key nghiệp vụ (VD: `"BN-2024-001"`) |
| `Payload` | string? | JSON raw nếu data nhỏ, null nếu dùng DownloadUrl |
| `DownloadUrl` | string? | URL download nếu data lớn, null nếu dùng Payload |
| `TotalRecords` | int | Số record trong batch |
| `ProcessedAt` | DateTime | Thời điểm lakehouse hoàn thành xử lý |

**Quy tắc:**
- Một trong hai `Payload` hoặc `DownloadUrl` phải có giá trị
- Nếu `Payload` là JSON array `[...]` → mỗi element tạo 1 snapshot riêng
- Nếu `Payload` là JSON object `{...}` → 1 snapshot duy nhất với `BusinessKey` từ event

---

## Entity: LakehouseSnapshot

```csharp
public sealed class LakehouseSnapshot : AggregateRoot<Guid>
{
    public string Namespace   // "lab-results"
    public string BusinessKey // "BN-2024-001"
    public string Payload     // JSONB — dữ liệu thực tế
    public string JobId       // truy vết batch nào ghi snapshot này
    public DateTime ReceivedAt
}
```

**Index:**
- `(Namespace, BusinessKey)` — query chính, composite
- `ReceivedAt` — lấy snapshot mới nhất
- `JobId` — debug/truy vết theo batch

---

## REST API

### `GET /lakehouse/snapshots/latest`

Lấy snapshot **mới nhất** của một record cụ thể. Đây là endpoint DynamicFormService DataSource gọi.

| Param | Required | Mô tả |
|-------|----------|-------|
| `namespace` | ✓ | Nhóm dữ liệu (VD: `lab-results`) |
| `key` | ✓ | Business key (VD: `BN-2024-001`) |

```
GET /lakehouse/snapshots/latest?namespace=lab-results&key=BN-2024-001
→ 200 { id, namespace, businessKey, payload: { hbA1c: 6.5, ... }, jobId, receivedAt }
→ 404 nếu chưa có snapshot
```

### `GET /lakehouse/snapshots`

Lấy danh sách snapshot theo namespace (mới nhất trước).

| Param | Required | Default | Mô tả |
|-------|----------|---------|-------|
| `namespace` | ✓ | — | Nhóm dữ liệu |
| `limit` | ✗ | 100 | Số lượng tối đa (max 1000) |

```
GET /lakehouse/snapshots?namespace=lab-results&limit=50
→ 200 [{ id, namespace, businessKey, payload, jobId, receivedAt }, ...]
```

---

## Tích hợp với DynamicFormService

Khi admin config screen trong DynamicFormService, khai báo DataSource trỏ vào LakehouseService:

```json
{
  "namespace": "labResults",
  "serviceId": "lakehouseservice",
  "resourcePath": "/lakehouse/snapshots/latest",
  "requiredParams": ["namespace", "key"]
}
```

FE binding expression:

```
{{sources.labResults.hbA1c}}
{{sources.labResults.bloodGlucose}}
{{sources.labResults.labDate}}
```

Khi FE load screen:
1. Đọc DataSources config từ DynamicFormService
2. Gọi song song: `GET /dm/records?...` và `GET /lakehouse/snapshots/latest?namespace=lab-results&key=BN-001`
3. Merge vào `sources` object
4. Render widget theo DataBinding expression

---

## Cấu hình

### Environment variables

```
ConnectionStrings__LakehouseDb=Host=postgres-lh;Port=5432;Database=LakehouseDb;Username=lh_user;Password=lh_pass
RabbitMq__Host=rabbitmq
RabbitMq__Port=5672
Jwt__Issuer=hdos-auth
Jwt__Audience=hdos-api
Jwt__Secret=<>=32 chars>
```

### Database

| Prop | Value |
|------|-------|
| Engine | PostgreSQL 16 |
| DB name | `LakehouseDb` |
| User | `lh_user` |
| Docker host | `postgres-lh:5432` |
| Host machine port | `5435` |

### nginx

- Swagger: `http://localhost:5000/lakehouse/swagger`
- API: `http://localhost:5000/lakehouse/...`

---

## Chạy local

```bash
# Khởi động với full stack
docker compose up -d

# Rebuild riêng
docker compose up -d --build lakehouseservice

# Xem logs
docker compose logs -f lakehouseservice

# Chạy migration thủ công
export DOTNET_ROOT="$HOME/.dotnet" && export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
dotnet ef database update \
  --project src/Services/LakehouseService/LakehouseService.Infrastructure \
  --startup-project src/Services/LakehouseService/LakehouseService.API
```

---

## Test gửi event thủ công

Dùng RabbitMQ Management UI (`http://localhost:15672`) để publish message vào exchange:

```json
{
  "jobId": "test-job-001",
  "namespace": "lab-results",
  "businessKey": "BN-2024-001",
  "payload": "{\"patientId\":\"BN-2024-001\",\"hbA1c\":6.5,\"bloodGlucose\":95,\"labDate\":\"2024-01-15\"}",
  "downloadUrl": null,
  "totalRecords": 1,
  "processedAt": "2024-01-15T10:00:00Z"
}
```

Sau đó query:

```bash
curl "http://localhost:5000/lakehouse/snapshots/latest?namespace=lab-results&key=BN-2024-001"
```

---

## Cấu trúc thư mục (Phase 1 — legacy)

```
src/Services/LakehouseService/
├── LakehouseService.Domain/
│   ├── Entities/LakehouseSnapshot.cs                ← legacy, sẽ xoá ở Phase 2 Bước 3
│   ├── Repositories/ILakehouseSnapshotRepository.cs ← legacy
│   └── Errors/LakehouseErrors.cs
├── LakehouseService.Application/
│   ├── DTOs/LakehouseSnapshotDto.cs                 ← legacy
│   ├── EventHandlers/LakehouseDataReadyHandler.cs   ← legacy
│   ├── Features/Snapshots/                          ← legacy
│   └── DependencyInjection.cs
├── LakehouseService.Infrastructure/
│   ├── Consumers/LakehouseDataReadyConsumer.cs      ← legacy
│   ├── Sync/                                        ← Phase 2: redirect publish event mới (doc 44)
│   ├── Persistence/
│   │   ├── LakehouseDbContext.cs
│   │   ├── LakehouseDbContextFactory.cs
│   │   ├── Configurations/LakehouseSnapshotConfiguration.cs   ← legacy
│   │   ├── Repositories/LakehouseSnapshotRepository.cs        ← legacy
│   │   └── Migrations/
│   └── DependencyInjection.cs
└── LakehouseService.API/
    ├── Controllers/SnapshotsController.cs           ← legacy
    ├── Program.cs
    ├── Dockerfile
    └── appsettings.json
```

> Phase 2 thêm: `Domain/Entities/ViewBinding.cs`, `API/Controllers/ViewBindingsController.cs`, `Application/Features/ViewBindings/*`. Xem doc 44 mục 4.

---

## Khi nào vẫn nên dùng Phase 1 (snapshot)

Phase 1 thiết kế cho trường hợp **producer ngoài Hdos không thể publish vào pipeline DataMatching**:

| Tình huống | Phase phù hợp |
|---|---|
| Producer external chỉ publish event với schema cố định (CloudEvents kiểu `LakehouseDataReadyIntegrationEvent`) — không sửa được code producer | Phase 1 — snapshot |
| Data dạng analytics aggregate, không cần dedup/match logic, không cần SourceProfile mapping | Phase 1 — snapshot |
| Data quá lớn (>10MB/record) cần cơ chế `DownloadUrl` thay vì inline payload | Phase 1 — snapshot (với cải tiến) |
| Mọi case còn lại — đặc biệt khi data có `business_key` rõ ràng và muốn rename field tự do | **Phase 2 — Unified Ingest (doc 44)** |

Trong codebase hiện tại, **không có producer external nào đang dùng `LakehouseDataReadyIntegrationEvent`**, nên Phase 1 sẽ được xoá hoàn toàn ở migration Bước 3 (xem doc 44 mục 8).
