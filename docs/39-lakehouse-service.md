# 39 — LakehouseService

## Tổng quan

`LakehouseService` là service trung gian nhận dữ liệu đã xử lý từ Lakehouse (qua RabbitMQ), lưu vào PostgreSQL dưới dạng snapshot JSONB, và expose REST API để DynamicFormService cùng FE query.

**Vấn đề giải quyết:** RabbitMQ là one-way push — FE không thể query ngược lại lakehouse theo yêu cầu. LakehouseService lưu snapshot để FE luôn có data đọc bất kỳ lúc nào.

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

## Cấu trúc thư mục

```
src/Services/LakehouseService/
├── LakehouseService.Domain/
│   ├── Entities/LakehouseSnapshot.cs
│   ├── Repositories/ILakehouseSnapshotRepository.cs
│   └── Errors/LakehouseErrors.cs
├── LakehouseService.Application/
│   ├── DTOs/LakehouseSnapshotDto.cs
│   ├── EventHandlers/LakehouseDataReadyHandler.cs
│   ├── Features/Snapshots/
│   │   ├── GetSnapshotByKey/
│   │   └── GetSnapshots/
│   └── DependencyInjection.cs
├── LakehouseService.Infrastructure/
│   ├── Consumers/LakehouseDataReadyConsumer.cs
│   ├── Persistence/
│   │   ├── LakehouseDbContext.cs
│   │   ├── LakehouseDbContextFactory.cs
│   │   ├── Configurations/LakehouseSnapshotConfiguration.cs
│   │   ├── Repositories/LakehouseSnapshotRepository.cs
│   │   └── Migrations/
│   └── DependencyInjection.cs
└── LakehouseService.API/
    ├── Controllers/SnapshotsController.cs
    ├── Program.cs
    ├── Dockerfile
    └── appsettings.json
```
