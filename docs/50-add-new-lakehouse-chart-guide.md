# 50 — BE Recipe: Thêm 1 báo cáo Path B (Direct SQL Lakehouse)

> **Mục đích.** Step-by-step viết 1 chart mới đọc trực tiếp từ lakehouse PG bằng
> raw SQL (Npgsql), KHÔNG qua StagingRecord / ingest. Endpoint trả về cùng JSON shape
> SduiPage với `/dm/pages/{code}` → FE renderer doc 48 dùng được luôn không cần đổi.
>
> Companion với:
> - [doc 48](./48-frontend-consume-dm-pages-chart-guide.md) — FE consume SduiPage
> - [doc 49](./49-add-new-sdui-page-config-guide.md) — Path A (qua StagingRecord)
> - **[doc 51](./51-charts-system-overview.md) — System overview (decision matrix, endpoint catalog)**
>
> **Khác biệt với Path A (doc 49):** không cần SourceProfile + ingest. Mỗi request
> chart = 1 SQL query live vào lakehouse PG.

---

## 0. TL;DR

```
[1] psql vào lakehouse, list cột view
[2] Quyết canonical name (PascalCase) cho mỗi cột
[3] (Optional) enroll SourceProfile để có convention reference
[4] Tạo file <Name>LakehouseChart.cs với raw SQL aliases canonical name
[5] DI register 1 dòng
[6] Build + rebuild docker + curl test
```

Tổng cộng **2 file đụng (1 new + 1 sửa DI)**, ~150-200 dòng code/chart.

Worked example: [`FinanceDailyLakehouseChart.cs`](../src/Services/LakehouseService/LakehouseService.Infrastructure/Charts/Configs/FinanceDailyLakehouseChart.cs).

---

## 1. Khi nào chọn Path B (doc này) thay vì Path A (doc 49)?

| | Path A `/dm/pages/{code}` | Path B `/lakehouse/charts/{code}` (doc này) |
|---|---|---|
| **Cần ingest data?** | ✅ Có (sync hoặc push) | ❌ Không |
| **SQL ở đâu?** | LINQ in-memory | **Raw SQL trong file C#** ✓ |
| **Realtime?** | Trễ theo chu kỳ sync | ✅ Live mỗi request |
| **JOIN nhiều view** | Phức tạp (LINQ cross-record) | ✅ Dễ với SQL JOIN |
| **Aggregate** | LINQ `.GroupBy()` in-memory | ✅ Server-side `GROUP BY` |
| **Share data module khác** | ✅ Có (DynamicForm DataSource đọc StagingRecord) | ❌ Không |
| Touch khi thêm chart | 2 file (Config + DI) | 2 file (Config + DI) |

**Pick Path B khi:**
- Chart độc lập, không share data với module khác
- Cần realtime / data mới nhất
- Muốn tận dụng SQL `GROUP BY`, `WINDOW`, `JOIN` mạnh
- Không muốn quản lý SourceProfile + sync infrastructure

---

## 2. Architecture flow

```
GET /lakehouse/charts/{code}?date=...&<filters>
        │
        ▼
LakehouseChartsController.Get(code, date, ct)
        │
        ▼
LakehouseChartBuilder.BuildAsync(code, date, query, ct)
        │
        ├─ [1] Lookup ILakehouseChartConfig bằng code  ← DI registry
        ├─ [2] Inject NpgsqlDataSource (warehouse PG)
        │
        ▼
config.BuildAsync(ds, date, query, ct)  ← BẠN IMPLEMENT
        │
        ├─ [a] Execute SQL query (1 hoặc nhiều) qua NpgsqlCommand
        ├─ [b] Map kết quả → record struct
        ├─ [c] Build SduiPage rows + components
        │
        ▼
return SduiPage (ApiResponse envelope)
```

`NpgsqlDataSource` đã được đăng ký Singleton ở `WarehouseSyncRegistration.cs:25` (cùng cái mà `WarehouseViewSyncer` dùng). Bạn chỉ cần inject.

---

## 3. Recipe step-by-step

Ví dụ mục tiêu: làm chart **lượt khám hàng ngày** từ view giả định `api.encounter_activity_daily`.

### Bước 1 — Discovery: tìm view + liệt kê cột

```bash
# SSH vào máy có psql access lakehouse PG
PGPASSWORD=<secret> psql -h <warehouse-host> -p 5432 -U <user> -d <db>
```

```sql
\d+ api.encounter_activity_daily
-- hoặc:
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema = 'api' AND table_name = 'encounter_activity_daily'
ORDER BY ordinal_position;
```

**Output mẫu:**
```
column_name             | data_type
date                    | date
department_id           | bigint
encounter_count         | bigint
new_patient_count       | bigint
discharge_count         | bigint
emergency_count         | bigint
```

→ Note xuống: tên view + cột nào số/string/date.

### Bước 2 — Quyết định canonical name

Convention: snake_case raw → PascalCase (giống auto-suggester):

| Raw column | Canonical name |
|---|---|
| `date` | `Date` |
| `department_id` | `DepartmentId` |
| `encounter_count` | `EncounterCount` |
| `new_patient_count` | `NewPatientCount` |
| `discharge_count` | `DischargeCount` |
| `emergency_count` | `EmergencyCount` |

### Bước 3 — (Optional) Enroll SourceProfile

Không bắt buộc cho Path B, nhưng nên làm để có **bản hợp đồng tên** dùng chung với Path A nếu sau này muốn ingest:

```bash
curl -k -X POST "https://localhost:8443/lakehouse/view-bindings/with-auto-profile" \
  -H 'Content-Type: application/json' \
  -d '{
    "viewName":            "api.encounter_activity_daily",
    "sourceSystem":        "lakehouse:encounter_activity_daily",
    "recordType":          "encounter-activity-daily",
    "businessKeyColumn":   "department_id",
    "pollIntervalSeconds": 300,
    "displayName":         "Encounter Activity Daily"
  }'
```

→ Response `mappings` là convention reference. Chart C# dùng đúng tên trong đó.

### Bước 4 — Quyết định layout

Tự hỏi 4 câu:

| Câu hỏi | Trả lời ví dụ | Widget |
|---|---|---|
| Show con số tổng nào? | Tổng lượt khám, BN mới, Xuất viện, Cấp cứu | **4 KpiCard** |
| So sánh nhóm gì? | Lượt khám / khoa | **ProgressList Top-N** |
| Outlier cần cảnh báo? | Khoa cấp cứu > 50 lượt | **AlertList** |
| Phân bổ tỷ lệ gì? | Hoạt động theo loại | **ChartPie** |

→ Đó là 3 row layout chuẩn.

### Bước 5 — Tạo file `<Name>LakehouseChart.cs`

Path: `src/Services/LakehouseService/LakehouseService.Infrastructure/Charts/Configs/<Name>LakehouseChart.cs`

```csharp
using Hdos.LakehouseService.Application.Charts.Sdui;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.Charts.Configs;

public sealed class EncounterActivityDailyLakehouseChart : ILakehouseChartConfig
{
    // ── [1] URL routing ─────────────────────────────────────
    public string Code => "encounter-activity-daily";

    // ── [2] SourceProfile convention (tham chiếu, không enforce) ──
    public const string SourceSystem = "lakehouse:encounter_activity_daily";
    public const string RecordType   = "encounter-activity-daily";
    private const string ViewName    = "api.encounter_activity_daily";

    public async Task<SduiPage> BuildAsync(
        NpgsqlDataSource ds, DateOnly date, IQueryCollection query, CancellationToken ct)
    {
        var totals = await FetchTotalsAsync(ds, date, ct);
        if (totals.EncounterCount == 0)
            return BuildEmpty(date);

        var perDept = await FetchPerDepartmentAsync(ds, date, ct);

        return new SduiPage(
            Code, "Lượt khám hàng ngày (Live)", "Live", true,
            $"Lakehouse trực tiếp · {DateTime.UtcNow.AddHours(7):HH:mm} · Ngày {date:dd/MM/yyyy}",
            [new("Xuất Excel", "default", null)],
            [
                BuildKpiRow(totals),
                BuildProgressAndAlertRow(perDept),
                BuildPieRow(totals),
            ],
            DateTime.UtcNow);
    }

    // ─── Record types — field name match SourceProfile canonical ───
    private sealed record Totals(
        int EncounterCount, int NewPatientCount, int DischargeCount, int EmergencyCount);
    private sealed record PerDept(
        int DepartmentId, int EncounterCount, int EmergencyCount);

    // ─── SQL queries — alias chuẩn canonical ───────────────
    private static async Task<Totals> FetchTotalsAsync(
        NpgsqlDataSource ds, DateOnly date, CancellationToken ct)
    {
        const string sql = $"""
            SELECT
                COALESCE(SUM(encounter_count),   0)::int AS "EncounterCount",
                COALESCE(SUM(new_patient_count), 0)::int AS "NewPatientCount",
                COALESCE(SUM(discharge_count),   0)::int AS "DischargeCount",
                COALESCE(SUM(emergency_count),   0)::int AS "EmergencyCount"
            FROM {ViewName}
            WHERE date = @d
        """;

        await using var conn   = await ds.OpenConnectionAsync(ct);
        await using var cmd    = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@d", date);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct)) return new Totals(0, 0, 0, 0);

        return new Totals(
            EncounterCount:  reader.GetInt32(reader.GetOrdinal("EncounterCount")),
            NewPatientCount: reader.GetInt32(reader.GetOrdinal("NewPatientCount")),
            DischargeCount:  reader.GetInt32(reader.GetOrdinal("DischargeCount")),
            EmergencyCount:  reader.GetInt32(reader.GetOrdinal("EmergencyCount")));
    }

    private static async Task<List<PerDept>> FetchPerDepartmentAsync(
        NpgsqlDataSource ds, DateOnly date, CancellationToken ct)
    {
        const string sql = $"""
            SELECT
                department_id                          AS "DepartmentId",
                COALESCE(SUM(encounter_count), 0)::int AS "EncounterCount",
                COALESCE(SUM(emergency_count), 0)::int AS "EmergencyCount"
            FROM {ViewName}
            WHERE date = @d
            GROUP BY department_id
            ORDER BY SUM(encounter_count) DESC
            LIMIT 20
        """;

        await using var conn   = await ds.OpenConnectionAsync(ct);
        await using var cmd    = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@d", date);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var list = new List<PerDept>();
        while (await reader.ReadAsync(ct))
            list.Add(new PerDept(
                DepartmentId:   reader.GetInt32(reader.GetOrdinal("DepartmentId")),
                EncounterCount: reader.GetInt32(reader.GetOrdinal("EncounterCount")),
                EmergencyCount: reader.GetInt32(reader.GetOrdinal("EmergencyCount"))));
        return list;
    }

    // ─── Section builders ──────────────────────────────────
    private static SduiRow BuildKpiRow(Totals t) =>
        new([
            new KpiCardComponent(6, new KpiCardProps("Lượt khám",     t.EncounterCount,  "#1677ff", "lượt", null)),
            new KpiCardComponent(6, new KpiCardProps("BN mới",        t.NewPatientCount, "#52c41a", "người", null)),
            new KpiCardComponent(6, new KpiCardProps("Xuất viện",     t.DischargeCount,  "#faad14", "lượt", null)),
            new KpiCardComponent(6, new KpiCardProps("Cấp cứu",       t.EmergencyCount,  "#ff4d4f", "lượt", null)),
        ]);

    private static SduiRow BuildProgressAndAlertRow(List<PerDept> rows)
    {
        int max = rows.Count > 0 ? rows.Max(x => x.EncounterCount) : 1;

        var items = rows.Take(15).Select(x => new ProgressItem(
            Label:          $"Khoa #{x.DepartmentId}",
            Value:          max > 0 ? Math.Round(x.EncounterCount * 100.0 / max, 1) : 0,
            SecondaryValue: null,
            Color:          x.EmergencyCount > 50 ? "#ff4d4f" : "#52c41a"
        )).ToList();

        var progress = new ProgressListComponent(16, new ProgressListProps(
            "Top 15 khoa theo lượt khám", null, 100, items, null));

        var alerts = rows.Where(x => x.EmergencyCount > 50)
            .OrderByDescending(x => x.EmergencyCount).Take(10)
            .Select(x => new AlertItem(
                $"K#{x.DepartmentId}", $"Cấp cứu {x.EmergencyCount} lượt",
                "—", $"Khoa #{x.DepartmentId}", "hôm nay", "warning"))
            .ToList();

        var alertList = new AlertListComponent(8, new AlertListProps(
            "Khoa nhiều cấp cứu", true, 400, alerts.Count, alerts));

        return new SduiRow([progress, alertList]);
    }

    private static SduiRow BuildPieRow(Totals t)
    {
        var pie = new ChartPieComponent(24, new ChartPieProps(
            "Phân bổ hoạt động", 280, "donut", true,
            [
                new("Bệnh nhân mới", t.NewPatientCount),
                new("Xuất viện",     t.DischargeCount),
                new("Cấp cứu",       t.EmergencyCount),
            ],
            ["#52c41a", "#faad14", "#ff4d4f"]));

        return new SduiRow([pie]);
    }

    private SduiPage BuildEmpty(DateOnly date) =>
        new(Code, "Lượt khám hàng ngày", "Trống", false,
            $"Không có dữ liệu ngày {date:dd/MM/yyyy}.",
            [], [], DateTime.UtcNow);
}
```

### Bước 6 — Đăng ký DI

File: `src/Services/LakehouseService/LakehouseService.Infrastructure/DependencyInjection.cs`

```csharp
services.AddSingleton<ILakehouseChartConfig, EncounterActivityDailyLakehouseChart>();
```

(Thêm dưới các config đã có như `BedOccupancyLakehouseChart`, `FinanceDailyLakehouseChart`.)

### Bước 7 — Build + deploy + test

```bash
# Local build verify
dotnet build src/Services/LakehouseService/LakehouseService.API

# Push
git add src/Services/LakehouseService/LakehouseService.Infrastructure/Charts/Configs/EncounterActivityDailyLakehouseChart.cs \
        src/Services/LakehouseService/LakehouseService.Infrastructure/DependencyInjection.cs
git commit -m "feat(charts): encounter-activity-daily Path B"
git push

# Server
ssh user@<server>
cd <repo>
git pull
docker compose up -d --build lakehouseservice
sleep 5

# List xem có code mới chưa
curl -k "https://<server>:8443/lakehouse/charts" | jq
# Expect array chứa "encounter-activity-daily"

# Render
curl -k "https://<server>:8443/lakehouse/charts/encounter-activity-daily?date=2026-05-02" \
  | jq '.data | {title, rowCount: (.rows|length), kpiCount: ([.rows[0].components[]] | length)}'
```

---

## 4. Worked example — `finance-daily` chart

File thật trong codebase: [`FinanceDailyLakehouseChart.cs`](../src/Services/LakehouseService/LakehouseService.Infrastructure/Charts/Configs/FinanceDailyLakehouseChart.cs).

### Quyết định chính

| Khía cạnh | Giá trị | Lý do |
|---|---|---|
| `Code` | `"finance-daily"` | Khớp record type cho dễ nhớ |
| `ViewName` | `"api.finance_daily"` | View đã có sẵn trong lakehouse |
| `SourceSystem` / `RecordType` | `"lakehouse:finance_daily"` / `"finance-daily"` | Convention auto-enroll |
| Số query | 3 (Totals + PerDept + PerBucket) | Mỗi widget cần aggregate khác nhau, push xuống DB |
| Filter động | `date` (path) + `?department=<id>` (query) | Demo cách parse filter từ `IQueryCollection` |

### SQL push-down pattern

Cùng widget logic nhưng aggregate ở SQL (không LINQ in-memory):

```sql
-- Totals KPI: 1 row 4 cột
SELECT SUM(total_invoice_amount)  AS "TotalInvoiceAmount",
       SUM(total_discount_amount) AS "TotalDiscountAmount",
       SUM(invoice_count)         AS "InvoiceCount",
       SUM(distinct_encounter_count) AS "DistinctEncounterCount"
FROM api.finance_daily
WHERE date = @d
  AND (@dept IS NULL OR department_id = @dept);

-- ProgressList: GROUP BY department_id
SELECT department_id AS "DepartmentId", SUM(...) AS ...
GROUP BY department_id
ORDER BY SUM(total_invoice_amount) DESC
LIMIT 30;

-- ChartPie: GROUP BY finance_bucket
SELECT finance_bucket AS "FinanceBucket", SUM(total_invoice_amount) AS "TotalInvoiceAmount"
GROUP BY finance_bucket
HAVING SUM(total_invoice_amount) > 0
ORDER BY SUM(total_invoice_amount) DESC
LIMIT 10;
```

→ Tận dụng `GROUP BY` của PG, không phải lôi 10k row về app rồi LINQ.

---

## 5. JOIN nhiều view trong 1 chart

Ví dụ: `finance_daily` có `department_id` (số) nhưng không có tên khoa. JOIN với `api.departments` (hoặc view khác có tên khoa):

```csharp
const string sql = $"""
    SELECT 
        f.department_id              AS "DepartmentId",
        d.department_name            AS "DepartmentName",     -- ← lấy từ view khác
        SUM(f.total_invoice_amount)  AS "TotalInvoiceAmount"
    FROM api.finance_daily f
    LEFT JOIN api.departments d                                 -- ← JOIN
           ON d.department_id = f.department_id
    WHERE f.date = @d
    GROUP BY f.department_id, d.department_name
    ORDER BY SUM(f.total_invoice_amount) DESC
    LIMIT 15;
""";
```

Path A không làm được kiểu này (ingest mỗi view riêng vào StagingRecord). **Đây là điểm mạnh đặc trưng của Path B.**

---

## 6. SQL best practice

| Nguyên tắc | Lý do |
|---|---|
| **Luôn dùng `@param`** | Chống SQL injection. Đừng concat string |
| **`COALESCE(SUM(x), 0)`** | Tránh NULL khi không có row matching |
| **`::int` / `::numeric` cast rõ ràng** | Tránh `BigInteger` ambiguous khi SUM |
| **`GROUP BY` ở DB**, không LINQ | Tận dụng index + engine PG |
| **`LIMIT` Top-N ở SQL** | Trang nhẹ + nhanh hơn LINQ `.Take()` |
| **`HAVING SUM(x) > 0`** | Loại slice rỗng cho ChartPie |
| **Alias `AS "PascalCase"`** | Match SourceProfile canonical convention |
| **Đọc bằng `GetOrdinal("Name")`** | Đổi thứ tự SELECT không vỡ code |
| **Raw string `"""..."""`** | Khỏi escape `\n`, dễ đọc |

---

## 7. Filter động qua `IQueryCollection`

`BuildAsync` nhận `IQueryCollection query` — mọi query string FE truyền vào lấy được:

```csharp
public async Task<SduiPage> BuildAsync(NpgsqlDataSource ds, DateOnly date, IQueryCollection query, CancellationToken ct)
{
    // Parse filter
    int?    deptId = int.TryParse(query["department"].FirstOrDefault(), out var d) ? d : null;
    string? bucket = query["bucket"].FirstOrDefault();
    bool    showZero = query["showZero"].FirstOrDefault() == "true";

    // Đẩy vào WHERE clause
    const string sql = $"""
        SELECT ...
        FROM {ViewName}
        WHERE date = @d
          AND (@dept IS NULL OR department_id = @dept)
          AND (@bucket IS NULL OR finance_bucket = @bucket)
        ...
    """;

    cmd.Parameters.AddWithValue("@dept",   (object?)deptId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@bucket", (object?)bucket ?? DBNull.Value);
}
```

→ FE gọi `?date=2026-06-08&department=3&bucket=invoice_type_3`.

Document filter nào chart hỗ trợ ở XML doc trên `Code` property.

---

## 8. Common errors

| Error | Nguyên nhân | Fix |
|---|---|---|
| 500 `relation "..." does not exist` | View name sai hoặc thiếu schema | Quote `api.<view>` chính xác |
| 500 `column "..." does not exist` | SQL alias sai chính tả | Compare với output `\d+ <view>` |
| 500 `cannot cast type X to int` | View column kiểu khác (bigint, numeric) | `::int` cast hoặc dùng `GetInt64` reader |
| 404 `Chart 'xxx' chưa đăng ký` | Quên DI register | Kiểm `DependencyInjection.cs` |
| Response trống `Rows: []` | Empty guard trigger nhưng filter sai | Check WHERE clause, query psql trực tiếp |
| Slow response (> 2s) | View không có index trên `date` | DE team thêm index hoặc cache layer |

---

## 9. Khi nào nâng cấp pattern?

Path B hiện tại tốt cho 90% case. Cân nhắc nâng cấp khi:

| Vấn đề | Nâng cấp |
|---|---|
| Mỗi request 1 SQL — high QPS gây tải PG | Cache response 30s với `IMemoryCache` |
| SQL phức tạp 100+ dòng, khó maintain | Tạo materialized view trong PG, query view này |
| Cần authorization theo user/role | Inject `IHttpContextAccessor` + filter theo claims |
| Cần share aggregate giữa nhiều chart | Tạo helper service `IFinanceAggregator.GetTotalsAsync(...)` |
| Khi SourceProfile mappings đổi liên tục | Runtime lookup mappings qua HTTP từ DataMatching (xem doc tương lai) |

---

## 10. Related docs

| Doc | Khi đọc |
|---|---|
| [48 — FE Consume /dm/pages](./48-frontend-consume-dm-pages-chart-guide.md) | FE side — render SduiPage shape |
| [49 — BE Path A SduiPageConfig](./49-add-new-sdui-page-config-guide.md) | Pattern qua StagingRecord (cần ingest) |
| [44 — Unified Ingest Pipeline](./44-unified-ingest-pipeline.md) | Hiểu vì sao Path A cần ingest |
| [45 — Auto-Enroll SourceProfile](./45-lakehouse-auto-sourceprofile.md) | Bước 3 với-auto-profile |

---

## 11. Query thẳng raw tables thay vì view

> Việc DE chưa tạo view? Schema warehouse phức tạp cần JOIN nhiều bảng?
> Path B vẫn dùng — chỉ cần thay `FROM api.<view>` → `FROM <schema>.<table>`.
> Không đụng pattern, không đụng infra.

### 11.1 Khi nào cần?

| Tình huống | Pick raw tables |
|---|---|
| DE team chậm, bạn muốn tự làm | ✓ |
| Cần JOIN 3-5 bảng với logic phức tạp | ✓ |
| Báo cáo ad-hoc, mỗi cái khác nhau | ✓ |
| Data volume lớn (> 10M row), cần materialized view | ✗ vẫn nên qua view |
| Schema warehouse hay đổi | ✗ vẫn nên qua view (cách ly) |

### 11.2 Workflow

```
[1] Discover raw tables
    psql lakehouse -c "\dt *.*"                       — list bảng
    psql lakehouse -c "\d+ raw.invoices"              — list cột table cụ thể

[2] Quy hoạch JOIN
    invoices (department_id) ↔ departments (id)
    invoices (invoice_date, department_id) ↔ encounters (encounter_date, department_id)

[3] Viết SQL trong chart C# (raw string """...""")

[4] Test SQL trong psql trước rồi paste vào file
```

### 11.3 Pattern code (xem [`FinanceDailyLakehouseChart.cs`](../src/Services/LakehouseService/LakehouseService.Infrastructure/Charts/Configs/FinanceDailyLakehouseChart.cs))

Khai báo table names ở `const string` đầu file để grep + replace dễ:

```csharp
public sealed class XLakehouseChart : ILakehouseChartConfig
{
    // ── TODO_TABLE: thay schema/table thật ──
    private const string InvoiceTable    = "raw.invoices";
    private const string DepartmentTable = "master.departments";
    private const string EncounterTable  = "raw.encounters";

    private static async Task<Totals> FetchTotalsAsync(NpgsqlDataSource ds, DateOnly date, ...)
    {
        var sql = $"""
            SELECT
                COALESCE(SUM(i.total_amount),    0)::numeric AS "TotalInvoiceAmount",
                COALESCE(SUM(i.discount_amount), 0)::numeric AS "TotalDiscountAmount",
                COUNT(i.id)::int                             AS "InvoiceCount",
                COUNT(DISTINCT e.encounter_id)::int          AS "DistinctEncounterCount"
            FROM {InvoiceTable} i                                                -- ← const interpolated
            LEFT JOIN {EncounterTable} e
                   ON e.department_id  = i.department_id
                  AND e.encounter_date = i.invoice_date                          -- ← JOIN có điều kiện
            WHERE i.invoice_date = @d
              AND (@dept IS NULL OR i.department_id = @dept)
        """;

        // ... execute như cũ ...
    }
}
```

→ Khi DE tạo view sau này, bạn chỉ cần đổi:
```csharp
private const string InvoiceTable = "api.finance_daily";  // hoặc view name mới
```
+ adjust SELECT/JOIN — không phải viết lại toàn bộ chart.

### 11.4 Pitfalls khi query raw tables

| Pitfall | Tránh bằng |
|---|---|
| Schema thay đổi → chart 500 silent | Test SQL trong psql trước mỗi lần deploy |
| JOIN cross-product → row count vọt lên | `COUNT(DISTINCT ...)` thay vì `COUNT(*)` |
| Aggregate sai do JOIN nhân row | Phân tách thành 2 query rồi merge ở C# nếu phức tạp |
| Performance kém (> 2s) | `EXPLAIN ANALYZE` trong psql; tìm cột chưa có index |
| Schema khác nhau giữa env | Const string + config theo env, hoặc tách table names ra `appsettings.json` |

### 11.5 So sánh trực quan

```sql
-- Cách cũ (qua view)
SELECT * FROM api.finance_daily WHERE date = @d;

-- Cách mới (raw tables)
SELECT 
    i.department_id, d.department_name,
    SUM(i.total_amount), SUM(i.discount_amount),
    COUNT(DISTINCT e.encounter_id)
FROM raw.invoices i
LEFT JOIN master.departments d ON d.id = i.department_id
LEFT JOIN raw.encounters e ON e.department_id = i.department_id
                           AND e.encounter_date = i.invoice_date
WHERE i.invoice_date = @d
GROUP BY i.department_id, d.department_name;
```

**Cùng kết quả** (giả sử view `api.finance_daily` cũng compute như cách mới). Khác biệt: cách mới BE control SQL hoàn toàn.

---

## 12. Changelog

- **2026-06-08** — Initial. Recipe 7 bước, worked example finance-daily, JOIN pattern, filter qua IQueryCollection, SQL best practices.
- **2026-06-08 (v2)** — Refactor `FinanceDailyLakehouseChart` từ query view → query raw tables (JOIN invoices + departments + encounters). Thêm §11 hướng dẫn workflow raw tables + pitfalls. Const `TODO_TABLE` để grep + replace dễ.
