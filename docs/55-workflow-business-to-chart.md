# 55 — Workflow: từ nghiệp vụ → chart hoạt động (decision tree)

> **Câu hỏi doc này trả lời:** Khi business yêu cầu *"Tôi cần dashboard X"*, làm cách nào để đi từ câu nói đó → code chạy → user thấy chart trên browser?
>
> **Companion:** doc 53 (architecture), doc 54 (BE+FE walkthrough hands-on), doc 51 (system overview).

---

## 0. Tinh thần

Đây KHÔNG phải tutorial code (xem doc 54). Đây là **decision tree** + **process** — giúp bạn ra quyết định đúng ở mỗi step trước khi mở IDE.

**Sai lầm thường gặp khi bỏ qua process này:**
- Đặt schema vào sai service → cross-service coupling.
- Source code raw SQL inline trong consumer → không reuse được, chart khác cần lại copy SQL.
- Tự define record mới khi đã có sẵn → schema drift.
- FE call thẳng DB qua proxy → bypass cả layer contract.

---

## 1. 8 bước từ "tôi muốn chart" → "user thấy chart"

```
┌─────────────────────────────────────────────────────────────────────┐
│ STEP 0: Business need                                               │
│   "Tôi muốn dashboard X hiển thị Y"                                 │
└─────────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────────┐
│ STEP 1: Phác thảo OUTPUT — vẽ mockup                                │
│   Bao nhiêu KPI? Bao nhiêu chart? List/table? Filter gì?           │
└─────────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────────┐
│ STEP 2: Liệt kê FIELDS cần để build output                          │
│   Mỗi KPI/chart cần data field nào? Aggregate kiểu gì?              │
└─────────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────────┐
│ STEP 3: Quyết định SERVICE OWNER                                    │
│   Data này thuộc service nào logic? (decision tree §3)              │
└─────────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────────┐
│ STEP 4: Quyết định SOURCE KIND                                      │
│   Raw SQL? View DB? Code-gen (demo)? RMQ event? API ngoài? (§4)     │
└─────────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────────┐
│ STEP 5: Code top-down (§5)                                          │
│   5.1 Schema record                                                 │
│   5.2 Contract class                                                │
│   5.3 (Optional) Validator                                          │
│   5.4 Demo source (luôn có — test trước khi đụng DB)                │
│   5.5 Production source (SQL/View/Event...)                         │
│   5.6 Chart consumer (build SduiPage)                               │
│   5.7 DI register                                                   │
└─────────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────────┐
│ STEP 6: Build + test endpoint qua curl (?source=demo)               │
└─────────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────────┐
│ STEP 7: FE — page Next.js, fetch URL, <SduiPageView>                │
└─────────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────────┐
│ STEP 8: Iterate với business — review, điều chỉnh KPI/format        │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 2. STEP 1-2: Mockup + Schema design

### 2.1 Vẽ mockup trước khi gõ code

Hãy ép business mô tả output ở dạng grid. Ví dụ:

**"Tôi muốn dashboard tài chính theo ngày"** — chưa đủ. Hỏi tiếp:

```
┌──────────────────────────────────────────────────────────────┐
│ Filter:  [date selector]  [department dropdown]              │
├──────────────────────────────────────────────────────────────┤
│ [KPI: Tổng DT]  [KPI: Tổng giảm]  [KPI: Hóa đơn]  [KPI: BN] │
├──────────────────────────────────────────────────────────────┤
│ Top 15 khoa theo DT                  Khoa giảm giá cao       │
│ [progress bar list]                  [alert list]            │
├──────────────────────────────────────────────────────────────┤
│ Dòng doanh thu                       Phân bổ loại HĐ         │
│ [flow pipeline]                      [donut chart]           │
└──────────────────────────────────────────────────────────────┘
```

→ Mockup này có 4 KPI + 1 progress list + 1 alert list + 1 flow + 1 pie = **3 SduiRow**.

### 2.2 Liệt kê fields cần

Từ mockup, list tất cả field cần ở row level (KHÔNG aggregate):

| Mockup section | Field cần | Aggregate trong Consumer |
|---|---|---|
| KPI: Tổng DT | TotalInvoiceAmount | SUM |
| KPI: Tổng giảm | TotalDiscountAmount | SUM |
| KPI: Hóa đơn | InvoiceCount | SUM |
| KPI: Lượt khám | DistinctEncounterCount | SUM |
| Top 15 khoa | DepartmentId, DepartmentName, TotalInvoiceAmount, TotalDiscountAmount | GROUP BY dept |
| Khoa giảm giá cao | (cùng) | dept WHERE pct >= 30 |
| Dòng doanh thu | (totals) | SUM |
| Pie loại HĐ | FinanceBucket, TotalInvoiceAmount | GROUP BY bucket |

→ **Schema:** `(InvoiceDate, DeptId, DeptName, TotalInvoiceAmount, TotalDiscountAmount, InvoiceCount, DistinctEncounterCount, FinanceBucket)`. 1 row = 1 tuple `(dept × bucket)` cho 1 ngày.

**Quy tắc:** Schema KHÔNG aggregate — để consumer làm. Source emit row "thô nhất có thể nhưng đủ để consumer aggregate".

---

## 3. STEP 3: Decision tree — chọn SERVICE OWNER

```
Q: Data này CHỦ YẾU đến từ đâu?

├─ Hospital warehouse / lakehouse (raw.invoices, master.departments, view api.*) 
│   → LakehouseService                      
│
├─ HIS/BHYT/external system → ingest qua RMQ → StagingRecord canonical
│   → DataMatchingService                    
│
├─ OrderService internal DB (đơn hàng nội bộ)
│   → OrderService                          
│
├─ Bệnh viện nghiệp vụ (cấp cứu, phòng khám, nhân sự trực)
│   → M01Service                             
│
├─ AuthService internal (users, sessions, login attempts)
│   → AuthService                            
│
├─ NotificationService internal (notification log)
│   → NotificationService                    
│
└─ DynamicFormService internal (form submissions)
    → DynamicFormService
```

**Quy tắc:** Service owner = service nào logic OWNS data đó. KHÔNG ĐƯỢC đặt contract của data X trong service Y nếu service Y chỉ "muốn dùng" data đó.

**Edge case — data cần JOIN cross-service:**
- Hỏi: có thể đưa cross-service JOIN xuống Lakehouse (warehouse) không?
- Nếu có: contract ở LakehouseService.
- Nếu không (cần realtime cross-service): chia thành 2 contract riêng, 2 service riêng, FE/BFF tự merge.

**❌ NEVER:** đặt schema chung vào `BuildingBlocks/Contracts/DataContracts/` (đó là FRAMEWORK only, không phải domain).

---

## 4. STEP 4: Decision tree — chọn SOURCE KIND

```
Q: Data hiện tồn tại ở đâu?

├─ Đã có view DB / lakehouse view (vd api.bed_occupancy)
│   → ViewSource (đọc view bằng raw SQL hoặc Npgsql)
│   → ƯU: dễ refactor schema view không cần đổi C# code
│   → NHƯỢC: trễ theo cycle materialized view refresh
│
├─ Có raw tables (vd raw.invoices) + cần aggregate phức tạp
│   → SqlSource (raw SQL + JOIN + GROUP BY ở DB-side)
│   → ƯU: realtime, tận dụng SQL engine
│   → NHƯỢC: SQL inline trong C# code, refactor schema đau
│
├─ Data đã ingest qua DataMatching → StagingRecord canonical
│   → StagingSource (đọc IStagingRecordRepository, parse CanonicalPayload JSON)
│   → ƯU: dùng SourceProfile mapping linh hoạt
│   → NHƯỢC: source này phải ở DataMatchingService (vì StagingRecord là internal)
│
├─ Data từ external API (provider, HIS, BHYT...)
│   → ApiSource (HttpClient gọi API, dạng list)
│   → ƯU: realtime, không cần ingest
│   → NHƯỢC: phụ thuộc API ngoài, latency, throttling
│
├─ Event-driven (RMQ message arrives → push qua contract)
│   → EventSource (consumer RMQ tích lũy state in-memory hoặc DB)
│   → Phức tạp — chỉ dùng khi thực sự cần streaming
│
└─ Test / chưa có data thật
    → DemoSource (in-memory fake rows) — LUÔN có cho mọi contract
    → Để FE test render trước khi DB ready
```

**Quy tắc:**
1. **LUÔN tạo `DemoSource` trước** — viết code không phụ thuộc DB → test endpoint nhanh, FE work song song.
2. **Production source thứ 2** (SQL/View/API/Event) — implement sau khi demo work.
3. **1 contract có thể có >=2 source** — caller chọn qua `?source=...` query param.

---

## 5. STEP 5: Code top-down (template từng layer)

Ví dụ áp dụng cho **"chart tài chính theo ngày"** (= `finance.daily.row`, service = Lakehouse, source kind = SQL + Demo, output = Chart SduiPage).

### 5.1 Schema record

```
Path:      src/Services/LakehouseService/LakehouseService.Application/
           DataContracts/Schemas/Finance/FinanceDailyRow.cs
Namespace: Hdos.LakehouseService.Application.DataContracts.Schemas.Finance
```

```csharp
public sealed record FinanceDailyRow(
    DateOnly InvoiceDate,
    int      DepartmentId,
    string   DepartmentName,
    decimal  TotalInvoiceAmount,
    decimal  TotalDiscountAmount,
    int      InvoiceCount,
    int      DistinctEncounterCount,
    string?  FinanceBucket);
```

### 5.2 Contract class

```csharp
using Hdos.Contracts.DataContracts;

public sealed class FinanceDailyContract : DataContract<FinanceDailyRow>
{
    public const string ContractCode = "finance.daily.row";
    public override string Code => ContractCode;
    public override string DisplayName => "Tài chính theo ngày × khoa (row-level)";
}
```

### 5.3 Validator (optional)

```csharp
public sealed class FinanceDailyValidator : IDataContractValidator<FinanceDailyRow>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public ValueTask<DataContractValidationResult> ValidateAsync(FinanceDailyRow row, CancellationToken ct)
    {
        // Validate field-level rules. Return Invalid với message rõ.
        // ...
    }
}
```

### 5.4 Demo source (LUÔN có)

```
Path: src/Services/LakehouseService/LakehouseService.Infrastructure/
      DataContracts/Sources/FinanceDailyDemoSource.cs
```

```csharp
public sealed class FinanceDailyDemoSource : IDataSource<FinanceDailyRow>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public string SourceCode   => "demo";

    public async IAsyncEnumerable<FinanceDailyRow> ReadAsync(
        DataContractQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        // Yield fake rows. Đủ đa dạng để FE test edge case
        // (alert cao discount, dept lớn dept nhỏ, multiple bucket).
        yield return new FinanceDailyRow(..., "BHYT");
        yield return new FinanceDailyRow(..., "Dịch vụ");
        // ...
    }
}
```

### 5.5 Production source (SQL)

```csharp
public sealed class FinanceDailySqlSource : IDataSource<FinanceDailyRow>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public string SourceCode   => "sql";

    private readonly NpgsqlDataSource _ds;
    public FinanceDailySqlSource(NpgsqlDataSource ds) { _ds = ds; }

    public async IAsyncEnumerable<FinanceDailyRow> ReadAsync(
        DataContractQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        var date = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var dept = query.GetInt("department");
        // SQL GROUP BY (date, dept, bucket) → 1 row per group
        // ...
    }
}
```

### 5.6 Chart consumer

```
Path: src/Services/LakehouseService/LakehouseService.Infrastructure/
      DataContracts/Consumers/FinanceDailyChartConsumer.cs
```

```csharp
public sealed class FinanceDailyChartConsumer : IDataConsumer<FinanceDailyRow, SduiPage>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public string ConsumerCode => "chart";

    public async Task<SduiPage> ConsumeAsync(
        IAsyncEnumerable<FinanceDailyRow> stream,
        DataContractQuery query,
        CancellationToken ct)
    {
        var rows = new List<FinanceDailyRow>();
        await foreach (var r in stream.WithCancellation(ct)) rows.Add(r);

        // Aggregate theo bảng STEP 2.2:
        var totals    = AggregateTotals(rows);     // SUM
        var perDept   = AggregatePerDept(rows);    // GROUP BY dept
        var perBucket = AggregatePerBucket(rows);  // GROUP BY bucket

        return new SduiPage(
            Code: "finance-daily",
            Title: "Tài chính theo ngày",
            Rows: [
                BuildKpiRow(totals),
                BuildProgressAndAlertRow(perDept),
                BuildFlowAndPieRow(totals, perBucket),
            ],
            // ...
        );
    }
}
```

### 5.7 DI register

```csharp
// LakehouseService.Infrastructure/DataContracts/Registration/DataContractsRegistration.cs
services
    .AddDataContract<FinanceDailyContract>()
    .AddDataSource<FinanceDailyRow, FinanceDailySqlSource>()
    .AddDataSource<FinanceDailyRow, FinanceDailyDemoSource>()
    .AddDataConsumer<FinanceDailyRow, SduiPage, FinanceDailyChartConsumer>()
    .AddDataContractValidator<FinanceDailyRow, FinanceDailyValidator>();
```

---

## 6. STEP 6: Test endpoint qua curl

```bash
# Build + restart Lakehouse:
dotnet build src/Services/LakehouseService/LakehouseService.API
docker compose up -d --build lakehouseservice

# Verify contract đã đăng ký:
curl -s http://localhost:5000/lakehouse/contracts | jq '.data[].code'
# → "finance.daily.row"

# Test demo source (không đụng DB):
curl -s 'http://localhost:5000/lakehouse/contracts/finance.daily.row/chart?source=demo' \
  | jq '.data.title, (.data.rows | length)'

# Test SQL source (cần raw tables):
curl -s 'http://localhost:5000/lakehouse/contracts/finance.daily.row/chart?source=sql&date=2026-06-09' \
  | jq '.data'
```

**Iterate:** Nếu output không đúng → sửa consumer/source → restart → curl lại. Đừng đụng FE cho đến khi BE đúng.

---

## 7. STEP 7: FE — page Next.js

Sau khi BE confirm output JSON đúng:

```tsx
// fe/FOXAI-HDOSv2/app/dashboards/finance-daily/page.tsx
"use client";

import { useEffect, useState } from "react";
import SduiPageView from "@/components/sdui/SduiPageView";

const CONTRACT = "finance.daily.row";

export default function FinanceDailyPage() {
  const [data,    setData]    = useState(null);
  const [source,  setSource]  = useState("demo");
  const [date,    setDate]    = useState(new Date().toISOString().slice(0, 10));

  useEffect(() => {
    fetch(`/api/proxy/lakehouse/contracts/${CONTRACT}/chart?source=${source}&date=${date}`)
      .then(r => r.json())
      .then(j => setData(j.data));
  }, [source, date]);

  return (
    <div className="p-6">
      <div className="flex gap-3 mb-4">
        <select value={source} onChange={e => setSource(e.target.value)}>
          <option value="demo">demo</option>
          <option value="sql">sql</option>
        </select>
        <input type="date" value={date} onChange={e => setDate(e.target.value)} />
      </div>
      {data && <SduiPageView page={data} />}
    </div>
  );
}
```

**Critical:** FE **KHÔNG** tự build chart layout — gọi BE → BE trả SduiPage JSON → SduiPageView render. Đổi chart sau này = đổi consumer ở BE, FE tự refresh.

---

## 8. STEP 8: Iterate với business

Review session với business stakeholder:
- "Số này tính đúng không?" → fix aggregation logic trong consumer.
- "Thêm filter status?" → thêm `DataContractQuery.Get("status")` trong source + UI dropdown.
- "Đổi màu dựa theo X?" → fix component props trong consumer (`Accent`, `Color`).

**KHÔNG sửa nếu business chưa duyệt mockup ở STEP 1.** Đó là nguồn gốc scope creep.

---

## 9. Bảng quyết định nhanh (cheat sheet)

| Câu hỏi business | Hỏi ngược lại | Output |
|---|---|---|
| "Cần dashboard tài chính" | Bao nhiêu KPI, chart, table? Filter gì? | Mockup grid |
| "Hiển thị doanh thu" | Theo ngày? Theo khoa? Theo loại HĐ? Tất cả? | Schema row fields |
| "Realtime" | Trễ <1s hay <1 phút OK? | Source kind (Sql vs View vs Staging) |
| "Có data trên warehouse rồi" | Tên view? Schema thật? | Lakehouse + ViewSource hoặc SqlSource |
| "Data từ HIS/BHYT" | Đã ingest qua DM chưa? | DataMatching + StagingSource |
| "Cần xuất Excel" | OK | Thêm `IDataConsumer<T, byte[]>` "csv" |
| "Form cần pre-fill từ data này" | OK | Thêm `IDataConsumer<T, FormPrefillResult>` "form-prefill" |
| "Tao thấy contract X bên Lakehouse cần dùng bên DynamicForm" | Qua HTTP hay tự define schema bên DynamicForm? | HTTP qua `/lakehouse/contracts/{code}/prefill` (recommended) hoặc DynamicForm tự define schema riêng |

---

## 10. Anti-patterns (LƯU Ý)

| ❌ Anti-pattern | ✅ Đúng |
|---|---|
| Schema record ở `BuildingBlocks/Contracts/DataContracts/{Domain}/` | Schema ở `{Service}.Application/DataContracts/Schemas/{Domain}/` |
| Chart code SQL inline rồi build SduiPage trong 1 file | Source emit row → Consumer build SduiPage (tách trách nhiệm) |
| Source aggregate luôn ở SQL → consumer chỉ map | Source emit row "thô" → consumer aggregate (consumer tận dụng được nhiều cách view khác) |
| 1 contract chỉ có SQL source, không có demo | Demo source LUÔN có — để FE test trước, debug nhanh |
| Cross-service: service B import schema từ service A | Service B gọi HTTP `/lakehouse/contracts/{code}/...` HOẶC B define schema riêng (loose coupling) |
| FE tự build chart layout (Recharts call) | FE gọi BE → BE trả SduiPage JSON → `<SduiPageView>` render. Đổi chart = sửa BE consumer |
| Skip mockup, code thẳng | Mockup business duyệt trước → fields list → code |

---

## Companion docs

- **doc 53** — Architecture Data Contract Engine (đọc khi cần hiểu sâu pattern)
- **doc 54** — Hands-on tutorial: smoke test + build patient.daily.new BE+FE
- **doc 48** — FE consume SDUI chart (SduiPageView component, type TypeScript)
- **doc 51** — Charts system overview (Path A vs B vs DataContract)
- **doc 41** — Provider/Operation catalog (FE consume contract qua catalog, không hardcode URL)
