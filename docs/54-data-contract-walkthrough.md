# 54 — Walkthrough: Smoke test pilot + Build contract mới end-to-end

> **Companion với:** doc 53 (Data Contract Engine architecture), doc 48 (FE consume chart), doc 50 (BE recipe Path B), doc 51 (charts system overview).
>
> **Audience:** Dev sau khi merge `feat/data-contracts-doc53` — muốn (1) verify pipeline chạy được, (2) tự thêm chart mới qua DataContract pattern.

---

## Phần 0 — Pre-requisites

```bash
# 1. Checkout branch (hoặc merge xong vào main):
git checkout feat/data-contracts-doc53

# 2. Build sạch (đã verify 0 errors, 2 pre-existing warnings):
dotnet build Hdos.sln

# 3. Tests pass (~117/0):
dotnet test Hdos.sln

# 4. Docker stack chạy (cần Lakehouse PG + RabbitMQ + Lakehouse service):
docker compose up -d sqlserver postgres-dm rabbitmq lakehouseservice datamatchingservice nginx
```

Verify services:
```bash
docker compose ps | grep -E "lakehouse|datamatching"
# Cả hai phải "Up"
```

---

## Phần 1 — Smoke test pilot `finance.daily.row` (~10 phút)

### Bước 1.1 — Đảm bảo Lakehouse đang chạy

Endpoint `/lakehouse/contracts` luôn ON — không cần bật flag.

```bash
docker compose up -d lakehouseservice
```

Check logs ổn:
```bash
docker compose logs -f lakehouseservice | grep -i "now listening"
```

### Bước 1.2 — List contracts đã đăng ký

```bash
curl -s http://localhost:5000/lakehouse/contracts | jq
```

Kết quả mong đợi:
```json
{
  "success": true,
  "data": [
    {
      "code": "finance.daily.row",
      "displayName": "Tài chính theo ngày × khoa (row-level)",
      "schemaTypeName": "FinanceDailyRow"
    }
  ]
}
```

→ Confirm registry hoạt động. Nếu list rỗng: DI chưa wire (recheck `AddLakehouseDataContracts()` trong `DependencyInjection.cs`).

### Bước 1.3 — Get chart từ **demo source** (không đụng DB)

```bash
curl -s 'http://localhost:5000/lakehouse/contracts/finance.daily.row/chart?source=demo&date=2026-06-09' \
  | jq '.data.code, .data.title, .data.rows | length'
```

Kết quả:
```
"finance-daily"
"Tài chính theo ngày (DataContract)"
3
```

3 row = KPI + ProgressList/AlertList + FlowPipeline/ChartPie. → JSON shape khớp endpoint cũ.

Test xem KPI cụ thể:
```bash
curl -s 'http://localhost:5000/lakehouse/contracts/finance.daily.row/chart?source=demo' \
  | jq '.data.rows[0].components[0].props'
```

```json
{
  "title": "Tổng doanh thu",
  "value": "5.2 tỷ",
  "accent": "#1677ff",
  "hint": "VNĐ",
  "hintColor": null
}
```

### Bước 1.4 — Get chart từ **SQL source**

Sẽ fail nếu raw tables (`raw.invoices`, `master.departments`, `raw.encounters`) chưa setup — đó là expected behavior. Verify error rõ ràng:

```bash
curl -s 'http://localhost:5000/lakehouse/contracts/finance.daily.row/chart?source=sql&date=2026-06-09' \
  | jq '.data.title, .data.subtitle'
```

Nếu raw tables exist + có data:
```
"Tài chính theo ngày (DataContract)"
"Qua DataContractGateway · 12:34 · Ngày 09/06/2026"
```

Nếu raw tables KHÔNG exist: HTTP 500 với log Npgsql `relation "raw.invoices" does not exist`. → Source chưa map đúng schema. Sửa TODO_TABLE trong `FinanceDailySqlSource.cs`.

### Bước 1.5 — Form prefill endpoint

```bash
curl -s 'http://localhost:5000/lakehouse/contracts/finance.daily.row/prefill?source=demo&limit=3' \
  | jq '.data'
```

```json
{
  "contractCode": "finance.daily.row",
  "rowCount": 3,
  "rows": [
    { "invoiceDate": "2026-06-09", "departmentId": 1, "departmentName": "Khoa Tim mạch",
      "totalInvoiceAmount": 780000000, "totalDiscountAmount": 120000000,
      "invoiceCount": 90, "distinctEncounterCount": 100, "financeBucket": "BHYT" },
    ...
  ]
}
```

→ Shape này DynamicForm FE có thể bind trực tiếp vào form field (qua Provider/Operation catalog doc 41/52).

### Bước 1.6 — FE quick check (không phải tutorial đầy đủ)

Trong FE Next.js, đổi URL trong page hiển thị `/dm/pages/finance-daily` cũ:

```typescript
// fe/FOXAI-HDOSv2/app/dashboards/finance-daily/page.tsx
// CŨ:
const url = "/api/proxy/dm/pages/finance-daily?date=2026-06-09";

// MỚI (gọi DataContract endpoint):
const url = "/api/proxy/lakehouse/contracts/finance.daily.row/chart?source=demo&date=2026-06-09";
```

JSON shape giống nhau (SduiPage) → component `<SduiPageView>` render không cần thay đổi.

✅ **Smoke test xong.** Endpoint cũ vẫn chạy 100%, endpoint mới chạy song song. Sang Phần 2.

---

## Phần 2 — Build contract mới `patient.daily.new` (~30-45 phút)

**Mục tiêu:** Chart "Bệnh nhân mới theo ngày × khoa" qua DataContract pattern.

**Schema:** Mỗi row = (RegisterDate, DepartmentId, DepartmentName, NewPatientCount, AgeAvg).

**Output:** Endpoint `GET /lakehouse/contracts/patient.daily.new/chart?source=demo&date=...` trả SduiPage; FE render trang `/dashboards/patient-daily-new`.

### Bước 2.0 — Quyết định service owner

Patient data thuộc service nào? Theo CLAUDE.md, `LakehouseService` đã handle warehouse data và chart. → **Patient daily new contract sẽ LIVE trong LakehouseService.Application** (service-local schema, không phải BuildingBlocks).

> ⚠️ Trước đây tôi đặt FinanceDaily ở `BuildingBlocks/Contracts/DataContracts/Finance/` — đó là **sai**.
> BuildingBlocks chỉ chứa FRAMEWORK + cross-cutting shapes. Schema nghiệp vụ thuộc service owner.

### Bước 2.1 — Schema record

```bash
mkdir -p src/Services/LakehouseService/LakehouseService.Application/DataContracts/Schemas/Clinical
```

File: `src/Services/LakehouseService/LakehouseService.Application/DataContracts/Schemas/Clinical/PatientDailyNewRow.cs`

```csharp
namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;

/// <summary>
/// 1 row số bệnh nhân ĐĂNG KÝ MỚI theo (ngày × khoa).
/// AgeAvg = trung bình tuổi của bệnh nhân mới trong nhóm đó.
/// Service-local — LakehouseService owns.
/// </summary>
public sealed record PatientDailyNewRow(
    DateOnly RegisterDate,
    int      DepartmentId,
    string   DepartmentName,
    int      NewPatientCount,
    double   AgeAvg);
```

### Bước 2.2 — Contract class

File: `src/Services/LakehouseService/LakehouseService.Application/DataContracts/Schemas/Clinical/PatientDailyNewContract.cs`

```csharp
using Hdos.Contracts.DataContracts;

namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;

public sealed class PatientDailyNewContract : DataContract<PatientDailyNewRow>
{
    public const string ContractCode = "patient.daily.new";

    public override string Code => ContractCode;
    public override string DisplayName => "Bệnh nhân đăng ký mới theo ngày × khoa";
}
```

### Bước 2.3 — Validator (optional nhưng nên có)

File: `src/Services/LakehouseService/LakehouseService.Application/DataContracts/Schemas/Clinical/PatientDailyNewValidator.cs`

```csharp
using Hdos.Contracts.DataContracts;

namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;

public sealed class PatientDailyNewValidator : IDataContractValidator<PatientDailyNewRow>
{
    public string ContractCode => PatientDailyNewContract.ContractCode;

    public ValueTask<DataContractValidationResult> ValidateAsync(
        PatientDailyNewRow row, CancellationToken ct)
    {
        var errors = new List<string>();

        if (row.DepartmentId <= 0)
            errors.Add($"DepartmentId must be positive (got {row.DepartmentId}).");

        if (string.IsNullOrWhiteSpace(row.DepartmentName))
            errors.Add("DepartmentName cannot be empty.");

        if (row.NewPatientCount < 0)
            errors.Add($"NewPatientCount cannot be negative (got {row.NewPatientCount}).");

        if (row.AgeAvg < 0 || row.AgeAvg > 150)
            errors.Add($"AgeAvg {row.AgeAvg} unrealistic (expected 0-150).");

        return ValueTask.FromResult(DataContractValidationResult.FromMessages(errors));
    }
}
```

### Bước 2.4 — Source A: Demo (in-memory fake data)

```bash
mkdir -p src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/Sources
```

File: `src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/Sources/PatientDailyNewDemoSource.cs`

```csharp
using System.Runtime.CompilerServices;
using Hdos.Contracts.DataContracts;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Sources;

public sealed class PatientDailyNewDemoSource : IDataSource<PatientDailyNewRow>
{
    public string ContractCode => PatientDailyNewContract.ContractCode;
    public string SourceCode   => "demo";

    public async IAsyncEnumerable<PatientDailyNewRow> ReadAsync(
        DataContractQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        var date = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var seed = new (int Id, string Name, int Count, double Avg)[]
        {
            (1, "Khoa Tim mạch",         42, 56.3),
            (2, "Khoa Hồi sức tích cực", 18, 62.1),
            (3, "Khoa Nhi",              87,  6.4),
            (4, "Khoa Sản",              35, 28.5),
            (5, "Khoa Cấp cứu",         124, 41.2),
            (6, "Khoa Ngoại thần kinh",  12, 58.7),
            (7, "Khoa Nội tiết",         28, 52.4),
            (8, "Khoa Da liễu",          15, 32.1),
        };

        foreach (var s in seed)
        {
            ct.ThrowIfCancellationRequested();
            yield return new PatientDailyNewRow(date, s.Id, s.Name, s.Count, s.Avg);
        }
    }
}
```

### Bước 2.5 — Source B: SQL (raw tables lakehouse)

File: `src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/Sources/PatientDailyNewSqlSource.cs`

```csharp
using System.Runtime.CompilerServices;
using Hdos.Contracts.DataContracts;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Sources;

/// <summary>
/// ⚠️ TODO_TABLE: thay placeholder bằng schema raw tables thật của bạn.
///   raw.patients.id, .register_date, .department_id, .birth_year
///   master.departments.id, .department_name
/// </summary>
public sealed class PatientDailyNewSqlSource : IDataSource<PatientDailyNewRow>
{
    public string ContractCode => PatientDailyNewContract.ContractCode;
    public string SourceCode   => "sql";

    private const string PatientTable    = "raw.patients";       // TODO_TABLE
    private const string DepartmentTable = "master.departments"; // TODO_TABLE

    private readonly NpgsqlDataSource _ds;

    public PatientDailyNewSqlSource(NpgsqlDataSource ds) { _ds = ds; }

    public async IAsyncEnumerable<PatientDailyNewRow> ReadAsync(
        DataContractQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var date   = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var deptId = query.GetInt("department");

        var sql = $"""
            SELECT
                p.register_date,
                p.department_id,
                COALESCE(d.department_name, 'Khoa #' || p.department_id) AS department_name,
                COUNT(p.id)::int                                          AS new_patient_count,
                COALESCE(AVG(EXTRACT(YEAR FROM CURRENT_DATE) - p.birth_year), 0)::float AS age_avg
            FROM {PatientTable} p
            LEFT JOIN {DepartmentTable} d ON d.id = p.department_id
            WHERE p.register_date = @d
              AND (@dept IS NULL OR p.department_id = @dept)
            GROUP BY p.register_date, p.department_id, d.department_name
            ORDER BY new_patient_count DESC NULLS LAST
        """;

        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@d",    date);
        cmd.Parameters.AddWithValue("@dept", (object?)deptId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            yield return new PatientDailyNewRow(
                RegisterDate:    reader.GetFieldValue<DateOnly>(0),
                DepartmentId:    reader.GetInt32(1),
                DepartmentName:  reader.GetString(2),
                NewPatientCount: reader.GetInt32(3),
                AgeAvg:          reader.GetDouble(4));
        }
    }
}
```

### Bước 2.6 — Consumer: Chart → SduiPage

File: `src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/Consumers/PatientDailyNewChartConsumer.cs`

```csharp
using Hdos.Contracts.DataContracts;
using Hdos.LakehouseService.Application.Charts.Sdui;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Consumers;

public sealed class PatientDailyNewChartConsumer
    : IDataConsumer<PatientDailyNewRow, SduiPage>
{
    public string ContractCode => PatientDailyNewContract.ContractCode;
    public string ConsumerCode => "chart";

    public async Task<SduiPage> ConsumeAsync(
        IAsyncEnumerable<PatientDailyNewRow> stream,
        DataContractQuery query,
        CancellationToken ct)
    {
        var rows = new List<PatientDailyNewRow>();
        await foreach (var r in stream.WithCancellation(ct)) rows.Add(r);

        var date  = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var total = rows.Sum(r => r.NewPatientCount);

        if (total == 0)
            return BuildEmpty(date);

        var weightedAge = rows.Sum(r => r.AgeAvg * r.NewPatientCount);
        var avgAge      = weightedAge / total;
        var topDept     = rows.OrderByDescending(r => r.NewPatientCount).First();

        return new SduiPage(
            Code:        "patient-daily-new",
            Title:       "Bệnh nhân đăng ký mới theo ngày",
            Badge:       "Contract",
            Live:        true,
            Subtitle:    $"Ngày {date:dd/MM/yyyy} · {rows.Count} khoa · qua DataContract",
            Actions:     [new("Xuất Excel", "default", null)],
            Rows: [
                BuildKpiRow(total, avgAge, topDept, rows.Count),
                BuildProgressRow(rows),
                BuildAgeDistributionRow(rows),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    private static SduiRow BuildKpiRow(int total, double avgAge, PatientDailyNewRow topDept, int deptCount) =>
        new([
            new KpiCardComponent(6, new KpiCardProps(
                Title: "Tổng BN mới", Value: total, Accent: "#1677ff",
                Hint: $"{deptCount} khoa", HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title: "Tuổi TB", Value: $"{avgAge:F1}", Accent: "#722ed1",
                Hint: "tuổi (weighted)", HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title: "Khoa đông nhất", Value: topDept.DepartmentName, Accent: "#52c41a",
                Hint: $"{topDept.NewPatientCount} BN", HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title: "Trung bình/khoa", Value: $"{(double)total / deptCount:F1}",
                Accent: "#13c2c2", Hint: "BN/khoa", HintColor: null)),
        ]);

    private static SduiRow BuildProgressRow(List<PatientDailyNewRow> rows)
    {
        var max = rows.Max(r => r.NewPatientCount);
        var items = rows
            .OrderByDescending(r => r.NewPatientCount)
            .Take(15)
            .Select(r => new ProgressItem(
                Label:          $"{r.DepartmentName} ({r.NewPatientCount})",
                Value:          max > 0 ? Math.Round((double)r.NewPatientCount * 100 / max, 1) : 0,
                SecondaryValue: r.AgeAvg,
                Color:          r.AgeAvg < 18 ? "#52c41a" : r.AgeAvg > 60 ? "#faad14" : "#1677ff"))
            .ToList();

        return new SduiRow([
            new ProgressListComponent(24, new ProgressListProps(
                Title: "Top khoa theo số BN mới (màu theo tuổi TB)",
                HeaderAction: null, MaxValue: 100, Items: items, FooterActions: null)),
        ]);
    }

    private static SduiRow BuildAgeDistributionRow(List<PatientDailyNewRow> rows)
    {
        var buckets = new[]
        {
            ("<18 tuổi",       rows.Where(r => r.AgeAvg < 18).Sum(r => r.NewPatientCount)),
            ("18-40 tuổi",     rows.Where(r => r.AgeAvg >= 18 && r.AgeAvg < 40).Sum(r => r.NewPatientCount)),
            ("40-60 tuổi",     rows.Where(r => r.AgeAvg >= 40 && r.AgeAvg < 60).Sum(r => r.NewPatientCount)),
            (">=60 tuổi",      rows.Where(r => r.AgeAvg >= 60).Sum(r => r.NewPatientCount)),
        };

        var pie = new ChartPieComponent(24, new ChartPieProps(
            Title: "Phân bổ tuổi (xấp xỉ theo tuổi TB khoa)",
            Height: 280, Variant: "donut", Legend: true,
            Data:   buckets.Where(b => b.Item2 > 0)
                           .Select(b => new ChartPieData(b.Item1, b.Item2)).ToList(),
            Colors: ["#52c41a", "#1677ff", "#faad14", "#ff4d4f"]));

        return new SduiRow([pie]);
    }

    private static SduiPage BuildEmpty(DateOnly date) => new(
        Code: "patient-daily-new", Title: "Bệnh nhân đăng ký mới", Badge: "Trống",
        Live: false, Subtitle: $"Không có BN mới ngày {date:dd/MM/yyyy}",
        Actions: [], Rows: [], GeneratedAt: DateTime.UtcNow);
}
```

### Bước 2.7 — Đăng ký DI

Edit `src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/Registration/DataContractsRegistration.cs`:

```csharp
using Hdos.Contracts.DataContracts.Extensions;
using Hdos.Contracts.DataContracts.FormPrefill;
using Hdos.LakehouseService.Application.Charts.Sdui;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;   // ← THÊM
using Hdos.LakehouseService.Application.DataContracts.Schemas.Finance;
using Hdos.LakehouseService.Infrastructure.DataContracts.Consumers;
using Hdos.LakehouseService.Infrastructure.DataContracts.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Registration;

public static class DataContractsRegistration
{
    public static IServiceCollection AddLakehouseDataContracts(this IServiceCollection services)
    {
        services.AddDataContracts();

        // ── finance.daily.row (đã có) ──
        services
            .AddDataContract<FinanceDailyContract>()
            .AddDataSource<FinanceDailyRow, FinanceDailySqlSource>()
            .AddDataSource<FinanceDailyRow, FinanceDailyDemoSource>()
            .AddDataConsumer<FinanceDailyRow, SduiPage,         FinanceDailyChartConsumer>()
            .AddDataConsumer<FinanceDailyRow, FormPrefillResult, FinanceDailyFormPrefillConsumer>()
            .AddDataContractValidator<FinanceDailyRow, FinanceDailyValidator>();

        // ── patient.daily.new (mới) ──                                ← THÊM CỤM NÀY
        services
            .AddDataContract<PatientDailyNewContract>()
            .AddDataSource<PatientDailyNewRow, PatientDailyNewDemoSource>()
            .AddDataSource<PatientDailyNewRow, PatientDailyNewSqlSource>()
            .AddDataConsumer<PatientDailyNewRow, SduiPage, PatientDailyNewChartConsumer>()
            .AddDataContractValidator<PatientDailyNewRow, PatientDailyNewValidator>();

        return services;
    }
}
```

### Bước 2.8 — Build + test

```bash
dotnet build src/Services/LakehouseService/LakehouseService.API
# Phải 0 errors

docker compose up -d --build lakehouseservice

# Verify contract appears in list:
curl -s http://localhost:5000/lakehouse/contracts | jq '.data[].code'
# "finance.daily.row"
# "patient.daily.new"             ← MỚI

# Test demo source:
curl -s 'http://localhost:5000/lakehouse/contracts/patient.daily.new/chart?source=demo&date=2026-06-09' \
  | jq '.data.title, .data.rows[0].components[0].props'

# Output:
# "Bệnh nhân đăng ký mới theo ngày"
# {
#   "title": "Tổng BN mới", "value": 361, "accent": "#1677ff", ...
# }
```

### Bước 2.9 — FE: Tạo page render chart mới

Vào FE repo `fe/FOXAI-HDOSv2/`. Pattern dựa trên doc 48 (SduiPageView component đã có sẵn).

**File mới:** `app/dashboards/patient-daily-new/page.tsx` (Next.js App Router):

```tsx
"use client";

import { useEffect, useState } from "react";
import SduiPageView from "@/components/sdui/SduiPageView"; // component có sẵn từ doc 48
import type { SduiPage } from "@/types/sdui";

const CONTRACT_CODE = "patient.daily.new";
const DEFAULT_SOURCE = "demo"; // đổi sang "sql" khi raw tables ready

export default function PatientDailyNewPage() {
  const [data,    setData]    = useState<SduiPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error,   setError]   = useState<string | null>(null);
  const [source,  setSource]  = useState(DEFAULT_SOURCE);
  const [date,    setDate]    = useState(
    new Date().toISOString().slice(0, 10)
  );

  async function fetchChart() {
    setLoading(true);
    setError(null);
    try {
      const url = `/api/proxy/lakehouse/contracts/${CONTRACT_CODE}/chart`
                + `?source=${source}&date=${date}`;
      const res  = await fetch(url, { cache: "no-store" });
      const json = await res.json();
      if (!json.success) throw new Error(json.message ?? "Unknown error");
      setData(json.data);
    } catch (e: any) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { fetchChart(); }, [source, date]);

  return (
    <div className="p-6 space-y-4">
      {/* Filter bar */}
      <div className="flex gap-3 items-center">
        <label>
          Source:&nbsp;
          <select value={source} onChange={e => setSource(e.target.value)}
                  className="border rounded px-2 py-1">
            <option value="demo">demo (fake data)</option>
            <option value="sql">sql (raw lakehouse)</option>
          </select>
        </label>
        <label>
          Date:&nbsp;
          <input type="date" value={date}
                 onChange={e => setDate(e.target.value)}
                 className="border rounded px-2 py-1" />
        </label>
        <button onClick={fetchChart} className="px-3 py-1 bg-blue-500 text-white rounded">
          Refresh
        </button>
      </div>

      {/* Chart */}
      {loading && <div>Loading…</div>}
      {error   && <div className="text-red-500">Error: {error}</div>}
      {data    && <SduiPageView page={data} />}
    </div>
  );
}
```

**Nếu chưa có `SduiPageView` component:** xem doc 48 §3-5 hoặc nginx proxy mapping cho `/api/proxy/lakehouse/...` (nginx route `/lakehouse/` đã có sẵn).

**Verify FE:**
```
1. cd fe/FOXAI-HDOSv2 && npm run dev
2. Mở http://localhost:3000/dashboards/patient-daily-new
3. Thấy 4 KPI cards + progress list + pie chart
4. Đổi Source dropdown → demo/sql → data refetch
```

### Bước 2.10 — (Optional) Thêm consumer thứ 2: CSV export

Cùng contract, consumer khác. File `PatientDailyNewCsvConsumer.cs`:

```csharp
public sealed class PatientDailyNewCsvConsumer : IDataConsumer<PatientDailyNewRow, byte[]>
{
    public string ContractCode => PatientDailyNewContract.ContractCode;
    public string ConsumerCode => "csv";

    public async Task<byte[]> ConsumeAsync(
        IAsyncEnumerable<PatientDailyNewRow> stream,
        DataContractQuery query,
        CancellationToken ct)
    {
        using var ms = new MemoryStream();
        using var sw = new StreamWriter(ms, Encoding.UTF8);
        await sw.WriteLineAsync("RegisterDate,DeptId,DeptName,NewPatientCount,AgeAvg");
        await foreach (var r in stream.WithCancellation(ct))
            await sw.WriteLineAsync(
                $"{r.RegisterDate},{r.DepartmentId},\"{r.DepartmentName}\","
              + $"{r.NewPatientCount},{r.AgeAvg:F2}");
        await sw.FlushAsync();
        return ms.ToArray();
    }
}
```

Đăng ký DI:
```csharp
.AddDataConsumer<PatientDailyNewRow, byte[], PatientDailyNewCsvConsumer>()
```

Thêm endpoint trong controller (hoặc tạo controller riêng) tương tự `/chart` nhưng return `FileContentResult(byteArr, "text/csv")`.

→ **Cùng 1 source (sql / demo), 2 consumer khác nhau.** FE chỉ cần đổi URL — bản chất "phễu" đa output.

---

## Phần 3 — Troubleshooting

### "Data contract 'patient.daily.new' is not registered."
- Quên gọi `.AddDataContract<PatientDailyNewContract>()` trong `DataContractsRegistration.cs`
- HOẶC chưa rebuild Lakehouse: `docker compose up -d --build lakehouseservice`

### "Schema type mismatch: contract expects PatientDailyNewRow, got X"
- Controller gọi `ConsumeAsync<X, SduiPage>` với type sai
- Reflection dispatch trong `DataContractChartController` đã handle — kiểm tra contract.SchemaType match record bạn define

### "Data source 'sql' for contract 'patient.daily.new' is not registered"
- Source class quên implement `IDataSource<PatientDailyNewRow>` đúng schema generic
- HOẶC quên `.AddDataSource<PatientDailyNewRow, PatientDailyNewSqlSource>()`

### SQL source throws "relation does not exist"
- Raw tables (`raw.patients`, `master.departments`) chưa có trong Lakehouse PG
- Fix `TODO_TABLE` constants trong source class hoặc tạo view tạm
- Workaround: dùng `?source=demo` cho dev

### FE 404 từ nginx proxy
- nginx.conf route `/lakehouse/` đã có (proxy → `lakehouseservice:8080`). Kiểm tra:
  ```bash
  docker compose exec nginx cat /etc/nginx/conf.d/default.conf | grep -A3 lakehouse
  ```
- Restart nginx nếu thay đổi config: `docker compose restart nginx`

### Build pass nhưng endpoint 404
- Contract chưa register trong DI — check `DataContractsRegistration.cs` (xem Bước 10)
- Check: `curl /lakehouse/contracts` — nếu list rỗng = chưa register contract nào

### Validator không chạy
- Validator OPTIONAL, Gateway chỉ gọi nếu caller invoke `gateway.ValidateAsync(...)`. Controller không auto-validate — caller responsibility.
- Nếu muốn auto-validate trước Consumer: gọi `gateway.ValidateAsync` trong custom controller before ConsumeAsync.

---

## Phần 4 — Checklist khi thêm contract mới (cheat sheet)

```
☐ 0. Quyết định SERVICE OWNER (Lakehouse / Order / M01 / DataMatching / ...)

# Schema (Application layer)
☐ 1. Tạo Domain folder: src/Services/{Service}/{Service}.Application/DataContracts/Schemas/{Domain}/
☐ 2. {Entity}Row.cs               record với fields canonical
☐ 3. {Entity}Contract.cs          class : DataContract<{Entity}Row>
☐ 4. (Optional) {Entity}Validator.cs : IDataContractValidator<{Entity}Row>

# Source (Infrastructure layer)
☐ 5. Source folder: src/Services/{Service}/{Service}.Infrastructure/DataContracts/Sources/
☐ 6. {Entity}DemoSource.cs        IDataSource<{Entity}Row>, SourceCode="demo"
☐ 7. {Entity}SqlSource.cs         IDataSource<{Entity}Row>, SourceCode="sql"
                                  (Optional: {Entity}ViewSource, {Entity}RmqSource...)

# Consumer (Infrastructure layer)
☐ 8. Consumer folder: src/Services/{Service}/{Service}.Infrastructure/DataContracts/Consumers/
☐ 9. {Entity}ChartConsumer.cs     IDataConsumer<{Entity}Row, SduiPage>
                                  (Optional: CsvConsumer, FormPrefillConsumer, ...)

# DI + endpoint
☐ 10. Edit DataContractsRegistration.cs — thêm cụm AddDataContract + AddDataSource* + AddDataConsumer*

☐ 11. dotnet build src/Services/{Service}/{Service}.API     # 0 errors
☐ 12. docker compose up -d --build {service}

# Test
☐ 13. Verify endpoint:
     curl -s /lakehouse/contracts | jq '.data[].code'
     curl -s /lakehouse/contracts/{code}/chart?source=demo | jq '.data.title'

# FE
☐ 14. FE: page app/dashboards/{name}/page.tsx — fetch URL + <SduiPageView page={data} />
☐ 15. Test trên browser, verify render + filter date/source dropdown

# (Optional) Tests
☐ 16. Validator test → tests/Hdos.{Service}.Tests/DataContracts/Schemas/{Domain}/{Entity}ValidatorTests.cs
     (tạo {Service}.Tests project nếu chưa có — copy pattern Hdos.LakehouseService.Tests)
```

⚠️ **KHÔNG đặt schema (Row, Contract, Validator) trong BuildingBlocks.** BuildingBlocks chỉ chứa framework + cross-cutting shapes.

---

## Companion docs

- **doc 53** — Data Contract Engine architecture (đọc trước nếu chưa hiểu pattern)
- **doc 48** — FE consume SDUI chart (SduiPageView component, type definitions)
- **doc 50** — BE Path B recipe (legacy pattern, sẽ dần migrate qua DataContract)
- **doc 51** — Charts system overview (decision matrix Path A vs B vs DataContract)
- **doc 52** — Embed chart vào DynamicForm (cách Provider catalog gọi `/lakehouse/contracts/{code}/prefill`)
