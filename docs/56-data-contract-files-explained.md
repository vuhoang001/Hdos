# 56 — Data Contract Engine: files BE đã thêm + ý nghĩa từng file

> **Audience:** Dev mới onboard hoặc FE team đọc để hiểu **cấu trúc backend mới**. Không phải tutorial hands-on (xem doc 54) và không phải workflow guide (xem doc 55) — doc này chỉ liệt kê & giải thích.
>
> **Câu hỏi doc này trả lời:** *"Khi BE implement Data Contract Engine, họ thêm file gì vào codebase và mỗi file làm gì?"*

---

## 🎯 Tóm lại — 3 nhóm file

| Nhóm | Mục đích | Khi nào viết | Số file |
|---|---|---|---|
| **A — Framework engine** | Core của hệ thống (chỉ Anh em viết 1 lần) | Setup engine ban đầu | 10 file |
| **B — Per-chart files** | Mỗi chart mới = lặp lại nhóm này | Mỗi khi business yêu cầu chart mới | 3-7 file/chart |
| **C — Wiring** | Gắn engine với service + endpoint | Setup service + sửa khi add chart | 3 file |

---

## NHÓM A — Framework engine (BuildingBlocks)

> **Quy tắc vàng:** BuildingBlocks chỉ chứa framework + cross-cutting shapes. **TUYỆT ĐỐI KHÔNG** đặt schema nghiệp vụ (Finance, Patient, Order, …) vào đây.

**Đường dẫn:** `src/BuildingBlocks/Contracts/DataContracts/`

### A1. Interfaces khai báo "một contract là gì"

| File | Là gì | Vai trò |
|---|---|---|
| **`IDataContract.cs`** | Interface | Mỗi schema cần có `Code` (string unique), `SchemaType` (typeof), `DisplayName` (UI label). |
| **`IDataSource.cs`** | Interface generic `<TSchema>` | Mỗi cách lấy data có 1 class implement: `ReadAsync(query, ct) → IAsyncEnumerable<TSchema>`. Source biết `ContractCode` mình thuộc về và `SourceCode` ("demo", "sql", "view"...). |
| **`IDataConsumer.cs`** | Interface generic `<TSchema, TOutput>` | Mỗi output kind có 1 class implement: nhận stream rows → trả output (SduiPage cho chart, byte[] cho CSV, FormPrefillResult cho form, …). Consumer biết `ContractCode` + `ConsumerCode`. |
| **`IDataContractValidator.cs`** | Interface generic `<TSchema>` | Optional. Validate 1 row có hợp lệ không. Caller gọi `gateway.ValidateAsync(row)` để check trước khi ingest. |

### A2. Helper types (cross-cutting, không thuộc domain nào)

| File | Là gì | Vai trò |
|---|---|---|
| **`DataContractQuery.cs`** | Record | Bọc query params (`date`, `department`, `source`, …) thành 1 object. Helpers: `GetDate()`, `GetInt()`, `GetBool()`. Truyền vào source/consumer. |
| **`DataContractException.cs`** | Exception classes | `DataContractNotFoundException`, `DataSourceNotFoundException`, `DataConsumerNotFoundException`, `DataContractSchemaMismatchException`. Controller catch → trả 404 với message rõ. |
| **`FormPrefill/FormPrefillResult.cs`** | Record | Shape generic cho form pre-fill output. List<Dict<string, object?>>. Cross-cutting — bất kỳ contract nào muốn expose qua form prefill đều trả shape này. |

### A3. Engine runtime (registry + gateway)

| File | Là gì | Vai trò |
|---|---|---|
| **`DataContractRegistry.cs`** | Class singleton | "Sổ tay" tra cứu `code → IDataContract`. Build 1 lần khi service start (DI inject `IEnumerable<IDataContract>`). Detect duplicate codes → throw startup. |
| **`DataContractGateway.cs`** | Class scoped | **PHỄU CHÍNH.** Mọi controller/worker gọi gateway để chạy 1 contract. Methods: `ReadAsync<T>()`, `ConsumeAsync<T, TOut>()`, `ValidateAsync<T>()`, `ListSources<T>()`, `ListConsumers<T,TOut>()`. Resolve source/consumer từ `IServiceProvider` (scope-aware). |

### A4. DI helpers

| File | Là gì | Vai trò |
|---|---|---|
| **`Extensions/DataContractServiceCollectionExtensions.cs`** | Static extension methods | `services.AddDataContracts()` — bootstrap Gateway + Registry. `AddDataContract<T>()`, `AddDataSource<T,S>()`, `AddDataConsumer<T,Out,C>()`, `AddDataContractValidator<T,V>()` — wire từng class. |

---

## NHÓM B — Per-chart files (mỗi chart = clone pattern)

> **Quy tắc đặt file:** Theo Clean Architecture của Hdos:
> - **Schema** (record + Contract + Validator) → `{Service}.Application/DataContracts/Schemas/{Domain}/` (Application layer, không phụ thuộc gì).
> - **Source** + **Consumer** (impl chạy thật, đụng DB/HTTP) → `{Service}.Infrastructure/DataContracts/Sources/` và `/Consumers/`.

### Ví dụ minh họa: `finance.daily.row`

**Service owner:** Lakehouse (vì data từ raw.invoices ở Lakehouse PG).

### B1. Schema layer (Application) — 3 file

**Đường dẫn:** `src/Services/LakehouseService/LakehouseService.Application/DataContracts/Schemas/Finance/`

| File | Là gì | Vai trò |
|---|---|---|
| **`FinanceDailyRow.cs`** | `record` | **1 row data trông như nào** — fields canonical: `(InvoiceDate, DeptId, DeptName, TotalAmount, …)`. Là "hình dáng" của data. Không method, chỉ properties. |
| **`FinanceDailyContract.cs`** | Class extends `DataContract<FinanceDailyRow>` | **Đặt tên cho schema** trong toàn hệ thống: `Code = "finance.daily.row"` (URL/registry tra cứu). |
| **`FinanceDailyValidator.cs`** | Class implement `IDataContractValidator<FinanceDailyRow>` | **Rules row hợp lệ**: amount >= 0, dept > 0, discount <= total, … Optional nhưng nên có cho data từ external. |

### B2. Source layer (Infrastructure) — 1 file/source kind

**Đường dẫn:** `src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/Sources/`

| File | Là gì | Vai trò |
|---|---|---|
| **`FinanceDailySqlSource.cs`** | Class implement `IDataSource<FinanceDailyRow>` | **Lấy data từ raw SQL** Lakehouse PG (GROUP BY dept, bucket). `SourceCode = "sql"`. Khi URL `?source=sql` → gateway pick file này. |
| **`FinanceDailyDemoSource.cs`** | Class implement `IDataSource<FinanceDailyRow>` | **Lấy data từ fake hardcode** (16 row in-memory). `SourceCode = "demo"`. Không đụng DB. Khi URL `?source=demo` → pick file này. **Tip:** LUÔN tạo `Demo` source trước để FE test render trước khi DB ready. |

### B3. Consumer layer (Infrastructure) — 1 file/output kind

**Đường dẫn:** `src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/Consumers/`

| File | Là gì | Vai trò |
|---|---|---|
| **`FinanceDailyChartConsumer.cs`** | Class implement `IDataConsumer<FinanceDailyRow, SduiPage>` | **Biến rows → SduiPage cho FE chart.** Aggregate totals, build KPI cards, progress list, pie chart. `ConsumerCode = "chart"`. URL `/chart` endpoint dùng consumer này. |
| **`FinanceDailyFormPrefillConsumer.cs`** | Class implement `IDataConsumer<FinanceDailyRow, FormPrefillResult>` | **Biến rows → flat dict cho FE form** prefill. `ConsumerCode = "form-prefill"`. URL `/prefill` endpoint dùng consumer này. |

### Ví dụ thứ 2: `patient.daily.new`

Đường dẫn tương tự (chỉ đổi `Finance` → `Clinical`), 5 file:

| Path | Vai trò |
|---|---|
| `LakehouseService.Application/DataContracts/Schemas/Clinical/PatientDailyNewRow.cs` | Schema row |
| `LakehouseService.Application/DataContracts/Schemas/Clinical/PatientDailyNewContract.cs` | `Code = "patient.daily.new"` |
| `LakehouseService.Application/DataContracts/Schemas/Clinical/PatientDailyNewValidator.cs` | Rules (count >= 0, age 0-150, malePct 0-100) |
| `LakehouseService.Infrastructure/DataContracts/Sources/PatientDailyNewDemoSource.cs` | 8 row mock (8 khoa với count + age + malePct đa dạng) |
| `LakehouseService.Infrastructure/DataContracts/Consumers/PatientDailyNewChartConsumer.cs` | Build SduiPage: 4 KPI + ProgressList màu theo tuổi + Donut tuổi distribution |

→ **Pattern y hệt FinanceDaily, chỉ đổi domain.** Đây là cái doc 55 §5 dạy.

---

## NHÓM C — Wiring (gắn engine với service)

### C1. Registration extension

**Đường dẫn:** `src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/Registration/DataContractsRegistration.cs`

| Vai trò | Chi tiết |
|---|---|
| Đăng ký contract nào Lakehouse đang own | Mỗi contract = 1 cụm `.AddDataContract<X>().AddDataSource<R,S>().AddDataConsumer<R,Out,C>().AddDataContractValidator<R,V>()` |
| Method extension `services.AddLakehouseDataContracts()` | Gọi từ `AddLakehouseInfrastructure()` trong `DependencyInjection.cs` |

**Hiện tại có 2 contract đăng ký:**
- `finance.daily.row` — 2 source (sql, demo) + 2 consumer (chart, form-prefill) + validator
- `patient.daily.new` — 1 source (demo) + 1 consumer (chart) + validator

### C2. API endpoint controller

**Đường dẫn:** `src/Services/LakehouseService/LakehouseService.API/Controllers/DataContractChartController.cs`

| Endpoint | Mô tả |
|---|---|
| `GET /lakehouse/contracts` | List tất cả contract đã đăng ký (metadata: code, displayName, schemaTypeName) |
| `GET /lakehouse/contracts/{code}/chart` | Render chart qua consumer "chart" (default), nhận `?source=...` chỉ định source |
| `GET /lakehouse/contracts/{code}/prefill` | Form pre-fill output qua consumer "form-prefill" |

**Đặc điểm:**
- Feature flag `DataContracts:EnableNewEndpoint` mặc định false. Nếu OFF → endpoint trả 404.
- **Reflection dispatch:** Controller không hard-code từng schema type. Nó đọc `contract.SchemaType` từ Registry → `MakeGenericMethod()` để gọi `gateway.ConsumeAsync<TSchema, SduiPage>(...)`.
- Wrap kết quả trong `ApiResponse<T>` (chuẩn của Hdos).

### C3. DataMatching helper (optional, không phải core)

**Đường dẫn:** `src/Services/DataMatchingService/DataMatchingService.Application/Services/DataContractIngestExtensions.cs`

| Vai trò | Chi tiết |
|---|---|
| Extension method | `core.IngestContractRowAsync<TSchema>(contractCode, row, sourceSystem, ...)` |
| Khi nào dùng | Caller (vd. external API push patient daily new vào DataMatching) có typed row → serialize → ingest pipeline. Không cần caller tự `JsonSerializer.Serialize()`. |

---

## 📂 Bonus — files đã SỬA (không phải thêm mới)

| File | Sửa gì | Tại sao |
|---|---|---|
| `BuildingBlocks/Contracts/Contracts.csproj` | Add `Microsoft.Extensions.DependencyInjection.Abstractions` package | Để DI helpers compile được |
| `LakehouseService.Infrastructure/DependencyInjection.cs` | Gọi `services.AddLakehouseDataContracts()` | Wire engine vào service composition root |
| `LakehouseService.API/appsettings.json` | Thêm `DataContracts.EnableNewEndpoint = false` | Feature flag mặc định OFF (an toàn rollout) |
| `LakehouseService.Infrastructure/Charts/ILakehouseChartConfig.cs` | Đánh `[Obsolete]` | Soft deprecate Path B cũ — endpoint vẫn chạy, có warning cho dev thấy migrate |
| `DataMatchingService.Application/Sdui/SduiPageConfig.cs` | Đánh `[Obsolete]` | Soft deprecate Path A cũ |
| Một số file impl của Path A/B | `#pragma warning disable CS0618` | Suppress warning ở wiring sites cũ — build vẫn pass 0 warning mới |

---

## 🎓 Cheat sheet cho FE team integrate

| Bạn cần | URL | Source/Consumer pick |
|---|---|---|
| List contract có sẵn | `GET /api/proxy/lakehouse/contracts` | — |
| Chart Finance daily (demo data) | `GET .../lakehouse/contracts/finance.daily.row/chart?source=demo` | source=`demo`, consumer=`chart` (default) |
| Chart Finance daily (raw SQL — cần raw.invoices) | `GET .../lakehouse/contracts/finance.daily.row/chart?source=sql&date=2026-06-09` | source=`sql`, consumer=`chart` |
| Chart Patient daily new | `GET .../lakehouse/contracts/patient.daily.new/chart?source=demo` | source=`demo`, consumer=`chart` |
| Form prefill Finance | `GET .../lakehouse/contracts/finance.daily.row/prefill?source=demo&limit=5` | consumer=`form-prefill` |
| Filter dept | `...?source=demo&department=3` | bất kỳ source nào hỗ trợ filter |

**JSON shape trả về:**
```json
{
  "success": true,
  "data": {
    "code": "finance-daily",
    "title": "...",
    "rows": [
      { "components": [{ "type": "KpiCard", "props": {...} }, ...] },
      { "components": [{ "type": "ProgressList", "props": {...} }] },
      { "components": [{ "type": "ChartPie", "props": {...} }] }
    ]
  }
}
```

**Component types FE cần render:** `KpiCard`, `ProgressList`, `AlertList`, `FlowPipeline`, `ChartPie`. Xem doc 48 cho TypeScript types + Next.js renderer pattern.

**Trigger endpoint:** Set env `DataContracts__EnableNewEndpoint=true` ở Lakehouse container (đã có config `false` mặc định trong appsettings.json).

---

## Companion docs

- **doc 53** — Architecture Data Contract Engine (deep dive pattern)
- **doc 54** — Walkthrough hands-on: smoke test pilot + build new contract BE+FE
- **doc 55** — Workflow decision tree: từ nghiệp vụ → working chart
- **doc 48** — FE consume SDUI chart (SduiPageView component, TypeScript types)
- **doc 51** — Charts system overview (Path A vs B vs DataContract)
