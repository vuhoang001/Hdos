# 23 — DataMatchingService

**DataMatchingService** là service nhận dữ liệu thô từ nhiều nguồn khác nhau (HIS, BHYT, phòng khám bên ngoài…), chuẩn hóa về schema chung, phát hiện trùng lặp, và tổng hợp báo cáo nghiệp vụ y tế.

Khác với các service còn lại dùng SQL Server, DataMatchingService dùng **PostgreSQL** riêng — phù hợp với khối lượng ghi lớn (ingest batch), và không phụ thuộc instance SQL Server chung.

---

## Vai trò trong hệ thống

```
Nguồn ngoài (HIS, CSV, API bên thứ ba)
        │  POST /dm/ingest/json
        │  POST /dm/ingest/file
        ▼
DataMatchingService :8080
  ├─ 1. Tìm SourceProfile (mapping rules)
  ├─ 2. Canonicalize payload     ← đổi tên field sang schema chuẩn
  ├─ 3. SHA-256 dedup            ← từ chối record trùng nội dung
  ├─ 4. Ghi StagingRecord        ← status = Pending → PostgreSQL
  │
  ├─ [MatchingWorker background] ← batch 50 record / 30 giây
  │       └─ Pending → Matched
  │
  └─ GET /dm/reports/{code}      ← báo cáo từ record đã Matched
```

---

## Domain Model

### StagingRecord — vòng đời record

```
Receive()
    │
    ▼ Pending
    │
    ▼ Processing  ← MatchingWorker bắt đầu xử lý
    │
    ├──► Matched   ← matched key được gán
    ├──► Duplicate ← phát hiện trùng business key
    └──► Failed    ← lỗi trong quá trình xử lý
```

| Field | Ý nghĩa |
|-------|---------|
| `SourceSystem` | Mã nguồn, ví dụ `"his-01"` |
| `RawPayload` | JSON gốc, giữ nguyên để audit |
| `CanonicalPayload` | JSON sau khi áp mapping (tên field chuẩn hóa) |
| `BusinessKey` | Khóa nghiệp vụ trích từ canonical |
| `PayloadHash` | SHA-256 của `RawPayload` — index, dùng để dedup |
| `MatchedKey` | `SourceSystem::BusinessKey` sau khi matched |
| `FailureReason` | Lý do nếu `Status = Failed` |

### SourceProfile — cấu hình nguồn dữ liệu

Mỗi nguồn phải đăng ký **một lần** trước khi ingest. Nó khai báo cách ánh xạ field của nguồn sang tên field chuẩn.

```json
{
  "sourceSystem": "his-01",
  "displayName": "HIS Bệnh viện A",
  "businessKeyField": "MaBenhNhan",
  "mappings": {
    "patient_id": "MaBenhNhan",
    "department":  "TenKhoa",
    "cost":        "TongChiPhi",
    "status":      "TrangThai"
  }
}
```

`businessKeyField` **phải** là một trong các value của `mappings`.

---

## API Endpoints

### POST `/dm/sources` — Đăng ký nguồn

```http
POST /dm/sources
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "displayName": "HIS Bệnh viện A",
  "businessKeyField": "MaBenhNhan",
  "mappings": {
    "patient_id": "MaBenhNhan",
    "department":  "TenKhoa",
    "cost":        "TongChiPhi",
    "status":      "TrangThai"
  }
}
```

### GET `/dm/sources` — Danh sách nguồn đã đăng ký

### POST `/dm/ingest/json` — Nạp 1 bản ghi

```http
POST /dm/ingest/json
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "payload": {
    "patient_id": "BN-001",
    "department": "Tim Mạch",
    "cost": 1500000,
    "status": "Xuất viện"
  }
}
```

**Response `202 Accepted`:**
```json
{
  "success": true,
  "data": {
    "id": "7816cf83-...",
    "sourceSystem": "his-01",
    "businessKey": "BN-001",
    "status": "Pending"
  }
}
```

**`409 Conflict`** nếu payload đã tồn tại (hash trùng).

### POST `/dm/ingest/file` — Nạp batch từ file

```http
POST /dm/ingest/file
Content-Type: multipart/form-data

sourceSystem=his-01
file=<file.json hoặc file.csv>
```

Giới hạn: **50 MB**. File JSON phải là array hoặc single object. CSV: dòng đầu là header.

**Lưu ý:** CSV parser không xử lý quoted fields có dấu phẩy bên trong — dùng JSON cho dữ liệu phức tạp.

### GET `/dm/reports/{reportCode}` — Báo cáo

Query params: `sourceSystem`, `from`, `to` (ISO datetime, tất cả optional).

| Code | Tên | Nhóm theo |
|------|-----|-----------|
| `chi-phi-theo-khoa` | Chi phí theo khoa | `TenKhoa` → SUM(`TongChiPhi`) |
| `benh-nhan-theo-khoa` | Bệnh nhân theo khoa | `TenKhoa` × `TrangThai` |
| `tong-hop-nguon` | Tổng hợp theo nguồn | `SourceSystem` |

Báo cáo chỉ tính trên record `Status = Matched`.

---

## Deduplication — SHA-256

Mỗi record được hash toàn bộ `RawPayload`:

```
SHA-256(UTF-8 bytes) → hex string 64 ký tự
```

- Index `IX_StagingRecords_PayloadHash` → `ExistsHashAsync` O(log n)
- Hash theo nội dung **gốc** — hai bản ghi cùng raw payload sẽ bị reject dù source khác nhau
- Nếu cần dedup theo canonical (sau khi mapping) thì đổi thành `ComputeHash(canonicalPayload)`

---

## MatchingWorker

`IHostedService` chạy trong cùng process với API, xử lý pending records định kỳ.

```
Mỗi {WorkerIntervalSeconds} giây (default 30):
  GetPendingBatchAsync(50)
  → record.MarkProcessing()
  → matchedKey = "{SourceSystem}::{BusinessKey}" (hoặc ::{PayloadHash} nếu không có BusinessKey)
  → record.MarkMatched(matchedKey)
  → SaveChangesAsync()
```

**Config:**
```json
{ "Matching": { "WorkerIntervalSeconds": 30 } }
```

> **Hiện tại là stub** — tạo composite key và mark Matched, chưa so khớp giữa các nguồn. Matching thực sự (golden record, cross-source linking) cần implement thêm.

---

## Database — PostgreSQL

Connection string key: `DataMatchingDb`. Container riêng `postgres-dm`, **không dùng chung SQL Server** với các service khác.

```
postgres-dm (PostgreSQL 16)
  └── Database: DataMatchingDb
      ├── SourceProfiles        ← UNIQUE (SourceSystem)
      ├── StagingRecords        ← INDEX (PayloadHash), INDEX (Status, ReceivedAt)
      ├── OutboxMessage         ← MassTransit EF Outbox
      ├── OutboxState
      └── InboxState
```

Migration tự apply khi service khởi động (10 lần retry, delay 3s).

**Kiểm tra DB trực tiếp:**
```bash
# Local
docker exec hdos-postgres-dm psql -U dm_user -d DataMatchingDb

# Xem staged records
\c DataMatchingDb
SELECT "SourceSystem", "BusinessKey", "Status", "MatchedKey"
FROM "StagingRecords"
ORDER BY "ReceivedAt" DESC
LIMIT 20;
```

---

## Kiến trúc nội bộ

```
Domain/
  Entities/     StagingRecord (AggregateRoot), SourceProfile (BaseEntity)
  Enums/        RecordStatus
  Repositories/ IStagingRecordRepository, ISourceProfileRepository, IDataMatchingUnitOfWork

Application/
  Features/
    Ingest/     IngestJsonCommand, IngestFileCommand
    Sources/    RegisterSourceCommand, GetSourcesQuery
    Reports/    GetReportQuery (3 built-in reports)
  DTOs/         SourceProfileDto, IngestResultDto, IngestBatchResultDto, ReportDto

Infrastructure/
  Persistence/  DataMatchingDbContext (Npgsql), EF configs, Repositories
  Workers/      MatchingWorker (IHostedService, batch 50, interval 30s)
  DI/           UseNpgsql + MassTransit EF Outbox (UsePostgres)

API/
  Controllers/  IngestController, SourcesController, ReportsController
  Program.cs    JWT, OTel, AddNpgSql health check, auto-migrate
```

---

## Chạy local

### Docker Compose (cách đơn giản nhất)

```bash
docker compose up -d
```

DataMatchingService và `postgres-dm` khởi động cùng stack. Swagger tại:
```
http://localhost:5000/dm/swagger
```

### dotnet run (debug)

```bash
# Khởi động postgres-dm trước
docker compose up -d postgres-dm rabbitmq

# Chạy service
cd src/Services/DataMatchingService/DataMatchingService.API
dotnet run
```

`appsettings.json` đã trỏ `Host=localhost;Port=5433` — đúng với port-forward của `postgres-dm`.

### Tạo migration mới

```bash
DOTNET_ROOT=~/.dotnet PATH="$HOME/.dotnet:$PATH" \
~/.dotnet/tools/dotnet-ef migrations add <TenMigration> \
  --project src/Services/DataMatchingService/DataMatchingService.Infrastructure \
  --startup-project src/Services/DataMatchingService/DataMatchingService.API \
  --output-dir Persistence/Migrations \
  --context DataMatchingDbContext
```

---

## Environment Variables

| Biến | Ý nghĩa | Ví dụ |
|------|---------|-------|
| `ConnectionStrings__DataMatchingDb` | PostgreSQL connection string | `Host=postgres-dm;Port=5432;Database=DataMatchingDb;Username=dm_user;Password=...` |
| `Matching__WorkerIntervalSeconds` | Chu kỳ MatchingWorker (giây) | `30` |
| `RabbitMq__Host` | RabbitMQ host | `rabbitmq` |
| `Jwt__Secret` | JWT signing key (chia sẻ với AuthService) | — |

---

## CI/CD

Được tích hợp đầy đủ vào CI/CD pipeline (xem [doc 10](./10-cicd-pipeline.md)):

- **`services.json`**: entry `datamatchingservice` → Dockerfile path
- **`.github/path-filters.yml`**: trigger rebuild khi `src/Services/DataMatchingService/**` thay đổi
- **`ci.yml`**: nằm trong `ALL` list → luôn build khi push lên `main`
- **`docker-compose.server.yml`**: image từ GHCR + `datamatchingservice.env` + `postgres-dm` (port đóng trên server)

**Setup trên server lần đầu:**

```bash
# Thêm vào /opt/hdos-prod/.env
POSTGRES_DM_PASSWORD=<strong-password>

# Tạo env file cho service
cat > /opt/hdos-prod/datamatchingservice.env << 'EOF'
ConnectionStrings__DataMatchingDb=Host=postgres-dm;Port=5432;Database=DataMatchingDb;Username=dm_user;Password=<password>
Matching__WorkerIntervalSeconds=30
EOF
```

---

## Trạng thái hiện tại

| Phần | Trạng thái | Ghi chú |
|------|-----------|---------|
| Domain (StagingRecord, SourceProfile) | ✅ | State machine đầy đủ |
| Ingest JSON + dedup SHA-256 | ✅ | Chạy production-ready |
| Ingest File (JSON + CSV batch) | ✅ | Max 50 MB |
| MatchingWorker | ⚠️ Stub | Tạo key, chưa match thực sự cross-source |
| 3 built-in reports | ✅ | chi-phi, benh-nhan, tong-hop |
| PostgreSQL + EF migrations | ✅ | Auto-apply khi startup |
| MassTransit Outbox (PostgreSQL) | ✅ Configured | Chưa có integration event nào được publish |
| Docker Compose (local) | ✅ | `postgres-dm` + `datamatchingservice` |
| CI/CD pipeline | ✅ | path-filter, GHCR build, server override |
| `[Authorize]` trên controllers | ❌ Comment-out | Bật lại trước production |
| Tests | ❌ Chưa có | — |

---

## Checklist trước production

- [ ] Bỏ comment `// [Authorize]` trên `IngestController`, `SourcesController`, `ReportsController`
- [ ] Tạo `datamatchingservice.env` trên server với PostgreSQL password thực
- [ ] Thêm `POSTGRES_DM_PASSWORD` vào `/opt/hdos-prod/.env` và `/opt/hdos-staging/.env`
- [ ] Implement matching logic thực trong `MatchingWorker`
- [ ] Cập nhật bảng Outbox trong [doc 21](./21-outbox-pattern.md) khi thêm integration events
