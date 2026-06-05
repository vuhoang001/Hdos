# 43 — Warehouse Sync → Lakehouse Pattern

> Tài liệu mô phỏng đầy đủ cách **kéo dữ liệu từ Data Warehouse external** (Postgres) vào `LakehouseService` của Hdos, và **phân chia trách nhiệm rõ ràng giữa team Data Engineering (DE) và team Backend (BE)**.

**Áp dụng cho:** Hệ thống đã có sẵn data warehouse (Postgres / SQL Server) chứa data đã được lakehouse pipeline xử lý. Bây giờ muốn đẩy data đó vào Hdos để FE render qua DynForm.

**Không áp dụng cho:**
- Realtime CDC streaming (xem doc 22)
- Producer là người ngoài tự push vào RabbitMQ (xem doc 39 — Lakehouse service phase 1)

---

## Mục lục

1. [Tổng quan](#1-tổng-quan)
2. [Phân chia trách nhiệm DE vs BE](#2-phân-chia-trách-nhiệm-de-vs-be)
3. [Mô phỏng end-to-end](#3-mô-phỏng-end-to-end)
4. [DE Reference](#4-de-reference)
5. [BE Reference](#5-be-reference)
6. [Contract giữa DE và BE](#6-contract-giữa-de-và-be)
7. [Checklist setup](#7-checklist-setup)
8. [Troubleshooting](#8-troubleshooting)
9. [Khi nào KHÔNG dùng pattern này](#9-khi-nào-không-dùng-pattern-này)

---

## 1. Tổng quan

### 1.1 Vấn đề cần giải quyết

Trong nhiều dự án y tế / doanh nghiệp, kiến trúc data có sẵn từ trước:

```
HIS, BHYT, file CSV...
         │
         ▼
   Data Lakehouse external
   (Spark/Databricks/Airflow/...)
         │ ETL → clean, dedup, transform
         ▼
   Data Warehouse external
   (Postgres/SQL Server/Snowflake/...)
         │
         ▼
   ??? Hdos cần data này để FE bác sĩ xem
```

Câu hỏi: **làm sao Hdos lấy data từ warehouse → cho FE?**

3 lựa chọn:

| Cách | Khi nào |
|---|---|
| FE/Service Hdos query thẳng warehouse | ❌ Vi phạm Database-per-Service (CLAUDE.md mục 8). Coupling chặt vào schema warehouse |
| Producer ngoài Hdos push RabbitMQ → LakehouseService (doc 39) | ✅ Khi có thể sửa code lakehouse pipeline |
| **Hdos chủ động pull từ warehouse → LakehouseService** | ✅ **Khi KHÔNG sửa được lakehouse pipeline. Đây là pattern tài liệu này** |

### 1.2 Architecture diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│ EXTERNAL (do team Data Engineering quản lý)                          │
│                                                                      │
│  ┌──────────────┐  ETL nightly  ┌──────────────────────────────┐   │
│  │ Lakehouse    │──────────────►│ Data Warehouse Postgres       │   │
│  │ (Spark/...)  │               │  Tables: fact_*, dim_*        │   │
│  └──────────────┘               │  VIEWS:  v_lab_results_v1     │ ◄─┼── DE viết
│                                 │          v_patient_metrics_v1  │   │   contract
│                                 └──────────┬────────────────────┘   │
└────────────────────────────────────────────│────────────────────────┘
                                             │ JDBC/ADO.NET
                                             │ (read-only)
                                             │ poll mỗi 5 phút
                                             ▼
┌──────────────────────────────────────────────────────────────────────┐
│ HDOS                                                                 │
│                                                                      │
│  ┌──────────────────────────────┐                                   │
│  │ WarehousePollerWorker        │ ◄── BE viết (BackgroundService)   │
│  │ (LakehouseService.Infra/Sync)│                                   │
│  └──────────┬───────────────────┘                                   │
│             │ publish IntegrationEvent                              │
│             ▼                                                        │
│         RabbitMQ                                                    │
│             │                                                        │
│             ▼                                                        │
│  ┌──────────────────────────────┐                                   │
│  │ LakehouseService (đã có sẵn) │                                   │
│  │  - Consumer                  │                                   │
│  │  - LakehouseSnapshots Postgres                                   │
│  │  - REST /lakehouse/...       │                                   │
│  └──────────┬───────────────────┘                                   │
│             │                                                        │
│             ▼                                                        │
│  ┌──────────────────────────────┐                                   │
│  │ DynForm Screen DataSource    │                                   │
│  │ → FE render form pre-filled  │                                   │
│  └──────────────────────────────┘                                   │
└──────────────────────────────────────────────────────────────────────┘
```

### 1.3 Vì sao pattern này tốt

| | Lợi ích |
|---|---|
| **Tách concerns** | DE owns warehouse + VIEW. BE owns Poller + REST. Không xâm phạm domain nhau |
| **Contract qua VIEW** | DE refactor bảng raw được, miễn VIEW giữ nguyên columns |
| **Loose coupling** | Đổi engine warehouse (Postgres → Snowflake) chỉ sửa Poller, không đụng LakehouseService |
| **Bậc tự do về deploy** | DE refresh data, BE deploy app — độc lập |
| **Tính toán đúng chỗ** | Aggregate / Window function chạy trong warehouse (gần data); Business rule chạy trong C# (gần app logic) |

---

## 2. Phân chia trách nhiệm DE vs BE

### 2.1 Bảng so sánh

| Trách nhiệm | DE (Data Engineering) | BE (Backend Hdos) |
|---|---|---|
| **Raw tables** | ✅ Sở hữu | ❌ Không đụng |
| **VIEWS** | ✅ Viết, optimize, versioning | ❌ Chỉ SELECT |
| **Aggregate / Window function** | ✅ SQL trong VIEW | ❌ Không viết LINQ aggregate |
| **Derived field** (BMI, score đơn giản) | Có thể trong VIEW | Có thể trong C# Domain — **tuỳ phức tạp** |
| **Business rule** (đủ điều kiện, cảnh báo) | ❌ | ✅ C# Domain layer |
| **Schema evolution** (thêm cột, đổi type) | ✅ Quản lý qua VIEW versioning | ❌ Chỉ theo VIEW contract |
| **Index, partitioning, materialized view** | ✅ Tự quyết | ❌ Không đụng |
| **Connection string warehouse** | Cấp `hdos_reader` (read-only) | ✅ Dùng nó trong Poller |
| **WarehousePollerWorker** | ❌ | ✅ Viết + maintain |
| **Sync state tracking** (`last_synced_at`) | ❌ | ✅ Lưu trong Hdos DB riêng |
| **Publish IntegrationEvent** | ❌ | ✅ Qua MassTransit |
| **LakehouseSnapshots Postgres** (Hdos) | ❌ | ✅ |
| **REST `/lakehouse/...`** | ❌ | ✅ |
| **DynForm DataSource manifest** | ❌ | ✅ Config qua admin API |
| **Performance tuning warehouse** | ✅ EXPLAIN, materialized view, index | ❌ |
| **Performance tuning Poller** | ❌ | ✅ Batch size, throttle, retry |
| **VIEW spec documentation** | ✅ Viết spec | ✅ Đọc + smoke test khi startup |

### 2.2 Quy tắc vàng

> **Tính toán càng gần data càng tốt.**
>
> Warehouse có hàng triệu row + index + parallel query → để aggregate trong VIEW.
> Hdos chỉ pull về N row đã tính sẵn → C# áp business rule đơn giản → trả REST.

| Loại tính toán | Đặt ở đâu | Vì sao |
|---|---|---|
| `SUM/AVG/COUNT GROUP BY` | SQL VIEW | Index + parallel query |
| `AVG OVER (PARTITION BY ... RANGE 30 DAY)` | SQL VIEW | DB engine tối ưu cho window |
| `BMI = weight / (height * height)` | SQL VIEW hoặc C# | Đơn giản, đặt đâu cũng được |
| `IsAbnormal = hbA1c >= 8.0 OR delta > 1.0` | C# Domain | Business rule có thể đổi |
| Format `dd/MM/yyyy`, currency | FE | Presentation, không phải data |

### 2.3 Contract là VIEW name + columns spec

Đây là điểm quan trọng nhất:

```
DE                                              BE
│                                              │
│   "Tôi cấp VIEW này:"                        │
│                                              │
│   VIEW warehouse.v_lab_results_v1            │
│   ├── business_key      TEXT NOT NULL        │
│   ├── hba1c             NUMERIC(4,1)         │
│   ├── avg_hba1c_30d     NUMERIC(4,1)         │
│   ├── bmi               NUMERIC(4,1)         │
│   ├── measurement_count INTEGER              │
│   ├── last_measured_at  TIMESTAMPTZ          │
│   └── updated_at        TIMESTAMPTZ NOT NULL │
│                                              │
│   "Cập nhật: mỗi 1h"                         │
│   "Đảm bảo: updated_at có index"             │──► "OK, tôi sẽ SELECT từ đó"
│                                              │     "Poll mỗi 5 phút bằng cột updated_at"
│                                              │     "Smoke test khi startup"
```

**Đổi schema VIEW = breaking change → đặt tên `_v2` mới**, không sửa `_v1` đang chạy.

---

## 3. Mô phỏng end-to-end

> Toàn bộ phần này có thể chạy được trên máy local. Dataset: lab results bệnh nhân (HbA1c, glucose, BMI).

### 3.1 Setup mock warehouse Postgres

DE chạy 1 container Postgres riêng làm "warehouse":

```bash
docker run -d \
  --name warehouse-postgres \
  --network hdos-net \
  -e POSTGRES_DB=warehouse \
  -e POSTGRES_USER=warehouse_admin \
  -e POSTGRES_PASSWORD=warehouse_pass \
  -p 5436:5432 \
  postgres:16-alpine
```

> Trong production thật, warehouse là cluster riêng (Aurora / Cloud SQL / ...). Container chỉ để dev.

### 3.2 DE: Tạo schema raw + VIEW

DE viết 3 SQL files lưu trong repo Hdos tại `sql/warehouse/`:

#### File 1: `sql/warehouse/001_schema.sql`

```sql
-- DE sở hữu — schema warehouse
CREATE SCHEMA IF NOT EXISTS warehouse;

-- Raw fact table: mỗi lần đo lab
CREATE TABLE warehouse.fact_lab_results (
    id              BIGSERIAL PRIMARY KEY,
    business_key    TEXT NOT NULL,           -- MaBenhNhan
    hba1c           NUMERIC(4,1),
    blood_glucose   NUMERIC(5,1),
    weight_kg       NUMERIC(5,2),
    height_m        NUMERIC(3,2),
    measured_at     TIMESTAMPTZ NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Index quan trọng để query nhanh
CREATE INDEX idx_lab_business_key_measured
    ON warehouse.fact_lab_results (business_key, measured_at DESC);

CREATE INDEX idx_lab_created_at
    ON warehouse.fact_lab_results (created_at);
```

#### File 2: `sql/warehouse/002_seed_data.sql`

```sql
-- Seed 1000 records cho 50 bệnh nhân (mỗi BN ~20 lần đo)
INSERT INTO warehouse.fact_lab_results
    (business_key, hba1c, blood_glucose, weight_kg, height_m, measured_at)
SELECT
    'BN-' || LPAD((1 + (i % 50))::TEXT, 4, '0')         AS business_key,
    ROUND((4.5 + RANDOM() * 5.5)::NUMERIC, 1)           AS hba1c,
    ROUND((70 + RANDOM() * 120)::NUMERIC, 1)            AS blood_glucose,
    ROUND((50 + RANDOM() * 40)::NUMERIC, 2)             AS weight_kg,
    ROUND((1.50 + RANDOM() * 0.30)::NUMERIC, 2)         AS height_m,
    NOW() - (RANDOM() * INTERVAL '180 days')            AS measured_at
FROM generate_series(1, 1000) i;
```

#### File 3: `sql/warehouse/003_view_v1.sql`

```sql
-- VIEW = contract DE↔BE. DE đảm bảo schema này stable cho v1.
-- Đổi breaking → tạo v_lab_results_v2, không sửa v1.

CREATE OR REPLACE VIEW warehouse.v_lab_results_v1 AS
WITH latest_per_patient AS (
    SELECT DISTINCT ON (business_key)
        business_key,
        hba1c,
        blood_glucose,
        weight_kg,
        height_m,
        measured_at      AS last_measured_at,
        created_at       AS updated_at        -- BE poll dựa cột này
    FROM warehouse.fact_lab_results
    ORDER BY business_key, measured_at DESC
),
aggregated_30d AS (
    SELECT
        business_key,
        AVG(hba1c)        AS avg_hba1c_30d,        -- window aggregate
        COUNT(*)          AS measurement_count_30d  -- count aggregate
    FROM warehouse.fact_lab_results
    WHERE measured_at > NOW() - INTERVAL '30 days'
    GROUP BY business_key
)
SELECT
    l.business_key,
    l.hba1c,
    l.blood_glucose,
    l.weight_kg,
    l.height_m,
    -- Derived field: BMI tính trong VIEW (đơn giản, ổn định)
    CASE WHEN l.height_m > 0
         THEN ROUND(l.weight_kg / (l.height_m * l.height_m), 1)
         ELSE NULL
    END                              AS bmi,
    COALESCE(a.avg_hba1c_30d, l.hba1c) AS avg_hba1c_30d,
    COALESCE(a.measurement_count_30d, 1) AS measurement_count_30d,
    l.last_measured_at,
    l.updated_at
FROM latest_per_patient l
LEFT JOIN aggregated_30d a USING (business_key);
```

Áp dụng tất cả:

```bash
docker exec -i warehouse-postgres psql -U warehouse_admin -d warehouse < sql/warehouse/001_schema.sql
docker exec -i warehouse-postgres psql -U warehouse_admin -d warehouse < sql/warehouse/002_seed_data.sql
docker exec -i warehouse-postgres psql -U warehouse_admin -d warehouse < sql/warehouse/003_view_v1.sql
```

Test thử:

```bash
docker exec -it warehouse-postgres psql -U warehouse_admin -d warehouse -c \
  "SELECT * FROM warehouse.v_lab_results_v1 LIMIT 5;"
```

```
 business_key | hba1c | blood_glucose | weight_kg | height_m | bmi  | avg_hba1c_30d | ...
--------------+-------+---------------+-----------+----------+------+---------------
 BN-0001      |   7.2 |         142.3 |     78.50 |     1.72 | 26.5 |           7.1 |
 BN-0002      |   5.8 |         105.0 |     65.20 |     1.65 | 24.0 |           5.9 |
 ...
```

#### File 4 (optional): `sql/warehouse/999_reader_role.sql`

```sql
-- DE cấp quyền read-only cho Hdos
CREATE USER hdos_reader WITH PASSWORD 'hdos_reader_pass';

GRANT USAGE ON SCHEMA warehouse TO hdos_reader;

-- Chỉ SELECT trên VIEW, KHÔNG cho phép đọc raw tables
GRANT SELECT ON warehouse.v_lab_results_v1 TO hdos_reader;

-- KHÔNG GRANT trên warehouse.fact_lab_results — chặn cứng
```

### 3.3 BE: Code WarehousePollerWorker

BE viết 5 file mới trong `LakehouseService.Infrastructure/Sync/`:

#### File 1: `Sync/IWarehouseSyncer.cs`

```csharp
namespace Hdos.LakehouseService.Infrastructure.Sync;

public interface IWarehouseSyncer
{
    Task<SyncResult> SyncAsync(CancellationToken ct);
}

public sealed record SyncResult(int RecordsProcessed, DateTime? NewLastSyncedAt);
```

#### File 2: `Sync/WarehouseSyncer.cs`

```csharp
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.Sync;

public sealed class WarehouseSyncer(
    NpgsqlDataSource warehouseDataSource,
    IEventBus eventBus,
    ISyncStateRepository syncState,
    ILogger<WarehouseSyncer> logger) : IWarehouseSyncer
{
    private const string Namespace = "lab-results";
    private const int    BatchSize = 1000;

    private const string Sql = """
        SELECT business_key,
               hba1c,
               blood_glucose,
               bmi,
               avg_hba1c_30d,
               measurement_count_30d,
               last_measured_at,
               updated_at
        FROM warehouse.v_lab_results_v1
        WHERE updated_at > @lastSync
        ORDER BY updated_at
        LIMIT @batchSize;
        """;

    public async Task<SyncResult> SyncAsync(CancellationToken ct)
    {
        var lastSync = await syncState.GetLastSyncAtAsync(Namespace, ct);
        var jobId    = $"sync-{Namespace}-{DateTime.UtcNow:yyyyMMddHHmmss}";

        await using var conn = await warehouseDataSource.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("lastSync",  lastSync);
        cmd.Parameters.AddWithValue("batchSize", BatchSize);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var count       = 0;
        DateTime? maxUpdated = null;

        while (await reader.ReadAsync(ct))
        {
            var businessKey = reader.GetString(0);
            var payload     = BuildPayloadJson(reader);
            var updatedAt   = reader.GetDateTime(7);

            await eventBus.PublishAsync(new LakehouseDataReadyIntegrationEvent(
                JobId:        jobId,
                Namespace:    Namespace,
                BusinessKey:  businessKey,
                Payload:      payload,
                DownloadUrl:  null,
                TotalRecords: 1,
                ProcessedAt:  updatedAt), ct);

            count++;
            if (maxUpdated is null || updatedAt > maxUpdated) maxUpdated = updatedAt;
        }

        if (maxUpdated is { } newLastSync)
            await syncState.SaveLastSyncAtAsync(Namespace, newLastSync, ct);

        logger.LogInformation(
            "Warehouse sync {Namespace}: {Count} records, lastSync {Old} → {New}",
            Namespace, count, lastSync, maxUpdated);

        return new SyncResult(count, maxUpdated);
    }

    private static string BuildPayloadJson(NpgsqlDataReader r) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            hbA1c               = r.IsDBNull(1) ? null : (decimal?)r.GetDecimal(1),
            bloodGlucose        = r.IsDBNull(2) ? null : (decimal?)r.GetDecimal(2),
            bmi                 = r.IsDBNull(3) ? null : (decimal?)r.GetDecimal(3),
            avgHbA1c30d         = r.IsDBNull(4) ? null : (decimal?)r.GetDecimal(4),
            measurementCount30d = r.GetInt32(5),
            lastMeasuredAt      = r.GetDateTime(6),
        });
}
```

#### File 3: `Sync/WarehousePollerWorker.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hdos.LakehouseService.Infrastructure.Sync;

public sealed class WarehousePollerWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WarehousePollerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("WarehousePollerWorker started, interval {Interval}", Interval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var syncer = scope.ServiceProvider.GetRequiredService<IWarehouseSyncer>();
                await syncer.SyncAsync(ct);
            }
            catch (OperationCanceledException) { /* graceful shutdown */ }
            catch (Exception ex)
            {
                logger.LogError(ex, "Warehouse sync iteration failed, will retry");
            }

            try   { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
```

#### File 4: `Sync/ISyncStateRepository.cs` + impl

```csharp
namespace Hdos.LakehouseService.Infrastructure.Sync;

public interface ISyncStateRepository
{
    Task<DateTime> GetLastSyncAtAsync(string ns, CancellationToken ct);
    Task SaveLastSyncAtAsync(string ns, DateTime value, CancellationToken ct);
}
```

```csharp
using Hdos.LakehouseService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hdos.LakehouseService.Infrastructure.Sync;

public sealed class SyncStateRepository(LakehouseDbContext db) : ISyncStateRepository
{
    public async Task<DateTime> GetLastSyncAtAsync(string ns, CancellationToken ct)
    {
        var row = await db.SyncStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Namespace == ns, ct);
        return row?.LastSyncedAt ?? DateTime.MinValue;
    }

    public async Task SaveLastSyncAtAsync(string ns, DateTime value, CancellationToken ct)
    {
        var row = await db.SyncStates.FirstOrDefaultAsync(s => s.Namespace == ns, ct);
        if (row is null)
            db.SyncStates.Add(new SyncState { Namespace = ns, LastSyncedAt = value });
        else
            row.LastSyncedAt = value;
        await db.SaveChangesAsync(ct);
    }
}

public sealed class SyncState
{
    public string   Namespace    { get; set; } = default!;   // PK
    public DateTime LastSyncedAt { get; set; }
}
```

Thêm `DbSet<SyncState>` vào `LakehouseDbContext`, EF migration `AddSyncState`.

#### File 5: `Sync/DependencyInjection.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.Sync;

public static class WarehouseSyncRegistration
{
    public static IServiceCollection AddWarehouseSync(
        this IServiceCollection services, IConfiguration config)
    {
        var warehouseConnStr = config.GetConnectionString("Warehouse")
            ?? throw new InvalidOperationException("ConnectionStrings__Warehouse not set");

        services.AddSingleton(_ => NpgsqlDataSource.Create(warehouseConnStr));
        services.AddScoped<ISyncStateRepository, SyncStateRepository>();
        services.AddScoped<IWarehouseSyncer, WarehouseSyncer>();
        services.AddHostedService<WarehousePollerWorker>();

        return services;
    }
}
```

Trong `LakehouseService.API/Program.cs`:

```csharp
builder.Services.AddWarehouseSync(builder.Configuration);
```

Trong `docker-compose.yml`, thêm env cho `lakehouseservice`:

```yaml
lakehouseservice:
  environment:
    ConnectionStrings__Warehouse: "Host=warehouse-postgres;Port=5432;Database=warehouse;Username=hdos_reader;Password=hdos_reader_pass"
```

### 3.4 HTTP test end-to-end

```bash
# 1. Seed thêm 5 row mới vào warehouse (giả lập DE pipeline chạy)
docker exec warehouse-postgres psql -U warehouse_admin -d warehouse -c "
  INSERT INTO warehouse.fact_lab_results (business_key, hba1c, blood_glucose, weight_kg, height_m, measured_at)
  SELECT 'BN-9999', 8.5, 180, 75, 1.70, NOW() - (i || ' min')::INTERVAL
  FROM generate_series(1, 5) i;
"

# 2. Đợi WarehousePollerWorker chạy (mặc định 5 phút), HOẶC restart service để trigger ngay
docker compose restart lakehouseservice

# 3. Xem log để chắc sync chạy thành công
docker compose logs lakehouseservice | grep "Warehouse sync"
# → Warehouse sync lab-results: 5 records, lastSync ... → ...

# 4. Query LakehouseService REST → verify data đã sync
curl -k "https://localhost:8443/lakehouse/snapshots/latest?namespace=lab-results&key=BN-9999"
```

Response mong đợi:

```json
{
  "success": true,
  "data": {
    "id": "...",
    "namespace": "lab-results",
    "businessKey": "BN-9999",
    "payload": "{\"hbA1c\":8.5,\"bloodGlucose\":180,\"bmi\":26.0,\"avgHbA1c30d\":8.5,\"measurementCount30d\":5,\"lastMeasuredAt\":\"...\"}",
    "jobId": "sync-lab-results-...",
    "receivedAt": "..."
  }
}
```

### 3.5 Tích hợp DynForm

Tạo Screen mới trỏ vào lakehouse:

```bash
# Tạo Screen "Hồ sơ bệnh nhân"
curl -k -X POST https://localhost:8443/forms/admin/screens \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d '{"moduleCode":"m01","code":"ho-so-bn","title":"Hồ sơ bệnh nhân","sortOrder":0}'

# Khai báo DataSource trỏ vào LakehouseService
curl -k -X PUT https://localhost:8443/forms/admin/screens/m01/ho-so-bn/data-sources \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d '[{
    "namespace": "labResults",
    "serviceId": "lakehouseservice",
    "resourcePath": "/lakehouse/snapshots/latest?namespace=lab-results&key={maBN}",
    "requiredParams": ["maBN"]
  }]'

# Tạo form + thêm field bind expression
# (Thêm các field key=hba1c, bmi với dataBindingExpression={{sources.labResults.hbA1c}})

# Verify layout response
curl -k "https://localhost:8443/forms/screens/m01/ho-so-bn/layout"
```

FE giờ chỉ cần truyền `maBN=BN-9999` qua route → form tự pre-fill.

---

## 4. DE Reference

### 4.1 4 loại tính toán đặt ở VIEW vs ngoài VIEW

| Loại | Ở VIEW? | Ví dụ |
|---|---|---|
| **Aggregate** | ✅ Luôn | `SUM(amount) GROUP BY khoa` |
| **Window function** | ✅ Luôn | `AVG OVER (PARTITION BY bn ORDER BY date RANGE 30 day)` |
| **Derived field đơn giản** | ✅ Nên | `BMI = weight/height²` |
| **Derived field phức tạp** (multi-step, gọi function ngoài) | ❌ Để C# | Vì hard to maintain in SQL |
| **Business rule** | ❌ Để C# | `IsAbnormal = ...` — thay đổi theo guideline |
| **Multi-source join** (warehouse + external API) | ❌ | Không thuộc DB |

### 4.2 Versioning VIEW

**Khi nào breaking, khi nào không:**

| Thay đổi | Breaking? | Cần v2? |
|---|---|---|
| Thêm cột mới | ❌ Không | Không |
| Đổi tên cột | ✅ Có | Có |
| Đổi data type cột | ✅ Có | Có |
| Xóa cột | ✅ Có | Có |
| Đổi nullability NULL → NOT NULL | ✅ Có | Có |
| Đổi nullability NOT NULL → NULL | ⚠️ Khả năng | Có nếu BE assume NOT NULL |
| Đổi semantics (`avg_hba1c_30d` từ "30d" → "7d") | ✅ Có | Có |
| Refactor query bên trong VIEW giữ nguyên columns | ❌ Không | Không |

**Quy trình tạo v2:**

```sql
-- Bước 1: Tạo v2 song song với v1
CREATE OR REPLACE VIEW warehouse.v_lab_results_v2 AS
  ...;

-- Bước 2: Cấp GRANT SELECT cho hdos_reader
GRANT SELECT ON warehouse.v_lab_results_v2 TO hdos_reader;

-- Bước 3: BE deploy code mới dùng v2

-- Bước 4: Sau khi BE migrate xong, drop v1
DROP VIEW warehouse.v_lab_results_v1;
REVOKE SELECT ON warehouse.v_lab_results_v2 FROM ...;
```

### 4.3 Performance trong warehouse

3 kỹ thuật phổ biến để VIEW chạy nhanh:

#### a) Materialized View khi VIEW phức tạp

```sql
CREATE MATERIALIZED VIEW warehouse.mv_lab_results_v1 AS
  SELECT ... FROM warehouse.fact_lab_results ...;

CREATE UNIQUE INDEX ON warehouse.mv_lab_results_v1 (business_key);

-- Refresh định kỳ
REFRESH MATERIALIZED VIEW CONCURRENTLY warehouse.mv_lab_results_v1;
```

Sau đó tạo VIEW wrapper:
```sql
CREATE OR REPLACE VIEW warehouse.v_lab_results_v1 AS
  SELECT * FROM warehouse.mv_lab_results_v1;
```

BE không cần biết MV — vẫn query `v_lab_results_v1`.

#### b) Partial index theo `updated_at`

Vì BE poll bằng `WHERE updated_at > @lastSync`, đảm bảo có index:

```sql
CREATE INDEX idx_lab_updated_at_recent
  ON warehouse.fact_lab_results (updated_at)
  WHERE updated_at > NOW() - INTERVAL '7 days';  -- partial
```

#### c) EXPLAIN ANALYZE

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM warehouse.v_lab_results_v1
WHERE updated_at > NOW() - INTERVAL '1 hour';
```

Nếu thấy `Seq Scan` thay vì `Index Scan` → thiếu index.

### 4.4 Document VIEW spec

DE phải để spec dạng file `.md` cùng với SQL:

```
sql/warehouse/
├── 001_schema.sql
├── 002_seed_data.sql
├── 003_view_v1.sql
└── specs/
    └── v_lab_results_v1.md  ← spec template ở section 6
```

---

## 5. BE Reference

### 5.1 BackgroundService template

`WarehousePollerWorker` ở section 3.3 là pattern chuẩn:

- Loop `while(!ct.IsCancellationRequested)`
- `try/catch` để 1 lần fail không kill worker
- `Task.Delay(Interval, ct)` để graceful shutdown
- `IServiceScopeFactory.CreateScope()` để dùng Scoped service trong Singleton worker

**Đừng:**
- Đừng dùng `Thread.Sleep` — block thread
- Đừng quên `ct` parameter — graceful shutdown sẽ không hoạt động
- Đừng throw exception ra ngoài `ExecuteAsync` — worker chết, không restart

### 5.2 Sync state — vì sao cần `last_synced_at`

Nếu không có:
- Mỗi lần poll lấy **toàn bộ** VIEW → query nặng + duplicate event
- Restart service → mất context, sync lại từ đầu

Có sync state:
- Chỉ pull row mới (`WHERE updated_at > lastSync`) → query nhẹ
- Restart vẫn pickup từ điểm cũ

**Lưu ở đâu?** Trong **Hdos DB của LakehouseService**, KHÔNG ở warehouse. Đây là state của Hdos, không phải của DE.

### 5.3 Connection pool warehouse

```csharp
services.AddSingleton(_ => NpgsqlDataSource.Create(warehouseConnStr));
```

`NpgsqlDataSource` là pool. Tối ưu:

```csharp
var builder = new NpgsqlConnectionStringBuilder(warehouseConnStr)
{
    MaxPoolSize = 10,            // Worker chạy 1 connection 1 lần, không cần nhiều
    MinPoolSize = 1,
    ConnectionLifetime = 600,    // recycle conn sau 10 min
    Timeout = 30,
};
services.AddSingleton(NpgsqlDataSource.Create(builder.ConnectionString));
```

**Đừng ăn hết slot của warehouse** — warehouse có nhiều consumer (BI tool, DE notebook, ...). Hdos chỉ cần ít connection.

### 5.4 Monitoring + Alerting

Thêm Prometheus metrics:

```csharp
public sealed class WarehouseSyncer(...)
{
    private static readonly Counter SyncedRows = Metrics.CreateCounter(
        "lakehouse_warehouse_synced_rows_total",
        "Total rows synced từ warehouse vào LakehouseSnapshots",
        new CounterConfiguration { LabelNames = new[] { "namespace" } });

    private static readonly Histogram SyncDuration = Metrics.CreateHistogram(
        "lakehouse_warehouse_sync_duration_seconds",
        "Thời gian 1 lần sync",
        new HistogramConfiguration { LabelNames = new[] { "namespace" } });

    public async Task<SyncResult> SyncAsync(CancellationToken ct)
    {
        using var timer = SyncDuration.WithLabels(Namespace).NewTimer();
        // ... existing code ...
        SyncedRows.WithLabels(Namespace).Inc(count);
        return new SyncResult(count, maxUpdated);
    }
}
```

Grafana alert (xem doc 08):
- `rate(lakehouse_warehouse_synced_rows_total[10m]) == 0` trong 30 phút → warehouse có thể chết hoặc VIEW rỗng
- `histogram_quantile(0.95, lakehouse_warehouse_sync_duration_seconds) > 30` → sync chậm bất thường

### 5.5 Error handling

3 lỗi điển hình + cách xử:

| Lỗi | Xử lý |
|---|---|
| `NpgsqlException: Connection refused` | Log warning, không throw, đợi lần sau |
| `PostgresException: 42P01 relation does not exist` | Log **error** (VIEW bị xóa!), thông báo cho ops |
| `RabbitMQ unreachable` (`MassTransitException`) | Worker retry tự động, không cần xử |

---

## 6. Contract giữa DE và BE

### 6.1 VIEW Spec template

DE viết file này, BE đọc và implement:

```markdown
# VIEW spec: warehouse.v_lab_results_v1

## Tổng quan
- **Mục đích:** Cung cấp snapshot mới nhất + aggregate 30 ngày cho lab results bệnh nhân
- **Frequency:** Refresh real-time (raw table có trigger), VIEW không cache
- **Owner:** team Data Engineering
- **Stable from:** 2026-06-05

## Columns

| Tên | Type | Nullable | Mô tả |
|---|---|---|---|
| `business_key`             | TEXT          | NO  | Mã bệnh nhân (canonical) |
| `hba1c`                    | NUMERIC(4,1)  | YES | HbA1c lần đo gần nhất |
| `blood_glucose`            | NUMERIC(5,1)  | YES | Glucose lần đo gần nhất (mg/dL) |
| `bmi`                      | NUMERIC(4,1)  | YES | BMI tính từ weight/height² |
| `avg_hba1c_30d`            | NUMERIC(4,1)  | YES | TB HbA1c trong 30 ngày gần nhất |
| `measurement_count_30d`    | INTEGER       | NO  | Số lần đo trong 30 ngày |
| `last_measured_at`         | TIMESTAMPTZ   | NO  | Thời điểm đo gần nhất |
| `updated_at`               | TIMESTAMPTZ   | NO  | Thời điểm row được DE tạo/sửa — BE dùng để poll |

## Index

- `business_key` (HASH index)
- `updated_at` (BTREE — quan trọng cho BE poll)

## Sample row

```json
{
  "business_key":            "BN-2024-001",
  "hba1c":                   7.2,
  "blood_glucose":           142.0,
  "bmi":                     24.5,
  "avg_hba1c_30d":           7.1,
  "measurement_count_30d":   3,
  "last_measured_at":        "2026-06-04T10:30:00Z",
  "updated_at":              "2026-06-04T10:30:05Z"
}
```

## Cam kết của DE

- ✅ `business_key` luôn match `MaBenhNhan` từ DataMatchingService
- ✅ `updated_at` đảm bảo MONOTONIC — không bao giờ giảm cho cùng business_key
- ✅ Refresh lag < 5 phút
- ✅ Báo trước 1 tuần nếu breaking change

## Cách BE dùng

```sql
SELECT * FROM warehouse.v_lab_results_v1
WHERE updated_at > :last_sync
ORDER BY updated_at
LIMIT 1000;
```
```

### 6.2 Smoke test khi BE startup

```csharp
// LakehouseService.API/Program.cs
app.MapHealthChecks("/health/warehouse-view", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("warehouse-view")
});

// LakehouseService.Infrastructure/Sync/WarehouseViewHealthCheck.cs
public sealed class WarehouseViewHealthCheck(NpgsqlDataSource ds) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext ctx, CancellationToken ct)
    {
        try
        {
            await using var conn = await ds.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "SELECT business_key, hba1c, updated_at FROM warehouse.v_lab_results_v1 LIMIT 1", conn);
            await cmd.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy("v_lab_results_v1 OK");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("v_lab_results_v1 FAILED", ex);
        }
    }
}
```

Đăng ký:
```csharp
services.AddHealthChecks()
    .AddCheck<WarehouseViewHealthCheck>("warehouse-view", tags: new[] { "warehouse-view" });
```

→ Nếu DE đổi VIEW phá contract, `/health/warehouse-view` báo `503` ngay khi Hdos startup → CI/CD fail deploy → ngăn data sai chảy ra production.

### 6.3 Version protocol

```
DE                                          BE
│                                           │
│ "Tôi sẽ tạo v2 vào 2026-07-01."          │
│ "Cấp GRANT SELECT v2 cho hdos_reader"    │
│ "v1 sẽ giữ thêm 30 ngày"                 │──► "OK, tôi merge code dùng v2 trong 2 tuần"
│                                           │     "Smoke test v2 pass"
│                                           │     "Deploy production v2"
│ "v1 đã không có ai gọi 7 ngày liên tiếp" │◄── "Xác nhận đã migrate xong"
│ "Tôi DROP v1"                            │
```

**Đo "không có ai gọi"** bằng `pg_stat_statements` hoặc Postgres audit log.

---

## 7. Checklist setup

### 7.1 DE checklist

```
[ ] Tạo schema `warehouse` trong DB warehouse
[ ] Tạo raw tables (fact_*, dim_*) — bình thường đã có
[ ] Tạo VIEW v_xxx_v1 với:
    [ ] Aggregate / window function nếu cần
    [ ] Derived field đơn giản (BMI, score)
    [ ] Column `updated_at` để BE poll incremental
[ ] Tạo user `hdos_reader` với password riêng
[ ] GRANT SELECT trên VIEW v_xxx_v1 cho hdos_reader
    (KHÔNG GRANT trên raw tables)
[ ] Tạo index hỗ trợ:
    [ ] (business_key) — query single record
    [ ] (updated_at) — incremental poll
[ ] Viết spec doc tại sql/warehouse/specs/v_xxx_v1.md
[ ] EXPLAIN ANALYZE query mẫu của BE — pass < 200ms
[ ] Cấp connection info (host, port, db, user, pass) cho BE
```

### 7.2 BE checklist

```
[ ] Đọc VIEW spec từ DE
[ ] Thêm ConnectionStrings__Warehouse vào appsettings + docker-compose env
[ ] Tạo folder LakehouseService.Infrastructure/Sync/
    [ ] IWarehouseSyncer + WarehouseSyncer
    [ ] WarehousePollerWorker (BackgroundService)
    [ ] ISyncStateRepository + SyncStateRepository
    [ ] DI extension AddWarehouseSync()
[ ] Thêm DbSet<SyncState> vào LakehouseDbContext + EF migration
[ ] Thêm WarehouseViewHealthCheck — smoke test khi startup
[ ] Thêm Prometheus metrics:
    [ ] lakehouse_warehouse_synced_rows_total{namespace}
    [ ] lakehouse_warehouse_sync_duration_seconds{namespace}
[ ] Test integration:
    [ ] Seed thêm row vào warehouse → đợi 5 phút → GET /lakehouse/snapshots/latest → verify
[ ] Tạo DynForm DataSource trỏ vào /lakehouse/snapshots/latest
[ ] Tạo Form + Field với expression {{sources.labResults.X}}
[ ] FE/Postman verify pre-fill thành công
[ ] Setup Grafana alert:
    [ ] No-sync trong 30 phút → warning
    [ ] Sync duration P95 > 30s → warning
[ ] Document trong README service: "warehouse sync chạy mỗi 5 phút"
```

---

## 8. Troubleshooting

### Lỗi 1: Sync luôn trả 0 record, không thấy data mới

**Triệu chứng:** log liên tục `Warehouse sync lab-results: 0 records`.

**Nguyên nhân + Fix:**

| Nguyên nhân | Check | Fix |
|---|---|---|
| `last_synced_at` đã pickup hết | `SELECT * FROM "SyncStates";` → giá trị mới nhất | Đúng, không phải bug. Đợi DE update warehouse |
| Cột `updated_at` ở VIEW không thay đổi | `SELECT MAX(updated_at) FROM v_xxx_v1;` | DE quên update cột này khi raw table thay đổi → fix VIEW |
| Permission `hdos_reader` bị revoke | `SELECT has_table_privilege('hdos_reader', 'warehouse.v_xxx_v1', 'SELECT');` | `GRANT SELECT ON warehouse.v_xxx_v1 TO hdos_reader;` |

### Lỗi 2: `PostgresException: 42P01 relation "warehouse.v_xxx_v1" does not exist`

DE đã DROP v1 nhưng BE chưa migrate sang v2. **Rollback BE về phiên bản cũ** hoặc deploy nhanh v2.

Phòng ngừa: enable smoke test ở section 6.2 → CI/CD fail trước khi deploy production.

### Lỗi 3: Sync chạy nhưng RabbitMQ không có message

Check `IEventBus.PublishAsync` có throw exception silent không.

```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Failed publish event for {Key}", businessKey);
    // QUYẾT ĐỊNH: throw để cả batch rollback? hay swallow để tiếp tục?
}
```

Recommend: log error nhưng tiếp tục batch, set `maxUpdated` chỉ cho row publish thành công.

### Lỗi 4: Sync quá chậm, P95 > 30s

```sql
-- DE check
EXPLAIN ANALYZE SELECT * FROM warehouse.v_xxx_v1 WHERE updated_at > '...' LIMIT 1000;
```

Nếu `Seq Scan` → thiếu index `updated_at`. Fix bằng partial index ở section 4.3.

Nếu VIEW phức tạp → đề xuất MV (materialized view).

### Lỗi 5: Duplicate event — cùng business_key publish nhiều lần

Bình thường — vì `updated_at` warehouse có thể thay đổi nhiều lần. `LakehouseService` consumer phải **idempotent** — upsert theo `(namespace, business_key)`, không insert mới mỗi lần.

Check `LakehouseSnapshots` table:
```sql
SELECT business_key, COUNT(*)
FROM "LakehouseSnapshots"
WHERE namespace = 'lab-results'
GROUP BY business_key HAVING COUNT(*) > 1;
```

Nếu có duplicate → fix consumer logic (`MERGE` thay vì `INSERT`).

---

## 9. Khi nào KHÔNG dùng pattern này

| Trường hợp | Pattern thay thế |
|---|---|
| Cần realtime < 5s | CDC (Debezium + Kafka) — xem doc 22 |
| Data analytical (BI dashboard) | Kết nối thẳng BI tool (Metabase / Superset / Power BI) vào warehouse — không qua Hdos |
| Data scientist ad-hoc query | Jupyter notebook với JDBC — không build app |
| Producer là người ngoài đã có sẵn code push | Họ tự publish RabbitMQ → LakehouseService consume — Cách 1 ở doc 39 |
| Volume > 10M row/ngày | Đừng pull về Hdos — query thẳng warehouse qua Trino/Federation |
| Data chỉ dùng 1 lần (export Excel) | Script Python query trực tiếp warehouse → file |
| Yêu cầu strict SOX/audit về data lineage | Cần data catalog + lineage tool (DataHub, Amundsen), không pattern này |

---

## Phụ lục — File checklist cho dev mới

```
sql/warehouse/                                    ← DE viết
├── 001_schema.sql
├── 002_seed_data.sql
├── 003_view_v1.sql
├── 999_reader_role.sql
└── specs/
    └── v_lab_results_v1.md

src/Services/LakehouseService/                    ← BE viết
├── LakehouseService.Infrastructure/
│   ├── Sync/                                    ← MỚI
│   │   ├── IWarehouseSyncer.cs
│   │   ├── WarehouseSyncer.cs
│   │   ├── WarehousePollerWorker.cs
│   │   ├── ISyncStateRepository.cs
│   │   ├── SyncStateRepository.cs
│   │   ├── WarehouseViewHealthCheck.cs
│   │   └── WarehouseSyncRegistration.cs
│   └── Persistence/
│       ├── LakehouseDbContext.cs                ← THÊM DbSet<SyncState>
│       └── Migrations/
│           └── 2026MMDD_AddSyncState.cs         ← MỚI
└── LakehouseService.API/
    └── Program.cs                               ← THÊM AddWarehouseSync()

docker-compose.yml                               ← THÊM env ConnectionStrings__Warehouse
docs/43-warehouse-sync-to-lakehouse.md           ← Tài liệu này
```

---

## Liên quan

- [22 — CDC với Debezium + Kafka](./22-cdc-debezium-kafka.md) — Realtime alternative
- [23 — DataMatchingService](./23-data-matching-service.md) — Provider khác cho DynForm
- [35 — Expression Data Binding](./35-expression-data-binding.md) — Cách DynForm bind data
- [36 — DataMatch → DynForm Flow](./36-datamatch-to-dynform-flow.md) — Pattern tương tự với DataMatching
- [39 — LakehouseService](./39-lakehouse-service.md) — Tổng quan service consume event
- [41 — Loose Coupling Architecture](./41-loose-coupling-architecture.md) — Provider Catalog cho DynForm
