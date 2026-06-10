# 58 — Lakehouse DataContract ↔ DynamicForm Integration

> **Hợp nhất hai engine**: dữ liệu sinh ra từ **Lakehouse DataContract** (doc 53/54/56) được nạp thẳng vào layout của **DynamicForm screen** (doc 29/33/41) — không hardcode URL, không phát minh pattern mới.
>
> Companion: doc 41 (Loose Coupling), doc 52 (Embed chart vào DynForm), doc 53 (Data Contract Engine), doc 54 (Walkthrough).

---

## 1. Tại sao có doc này

Trước doc 58:

| Bạn có | Bạn KHÔNG có |
|--------|--------------|
| DataContract `finance.daily.row` chạy được, trả prefill / chart JSON | DynamicForm không "thấy" được Lakehouse — admin tạo screen không có lựa chọn "lấy dữ liệu từ contract" |
| DynamicForm có Provider/Operation catalog (doc 41) | Bảng `Providers` trống — chưa có entry `lakehouse` |
| Expression binding `{{sources.ns.field}}` (doc 35) | Consumer trả `rows: [...]` (array) — expression ngầm hiểu object phẳng |

→ Bridge nằm ở **2 chỗ**: (a) seed Provider catalog, (b) thêm shape `single` cho prefill consumer.

---

## 2. Kiến trúc 2-tầng đăng ký

```
┌─────────────────── TIER 1: Lakehouse internal ────────────────────┐
│                                                                    │
│   DataContractsRegistration.cs (DI code, tại compile time)         │
│     ├── AddDataContract<FinanceDailyContract>()                    │
│     ├── AddDataSource<FinanceDailyRow, FinanceDailySqlSource>()    │
│     └── AddDataConsumer<..., FinanceDailyFormPrefillConsumer>()    │
│                                                                    │
│   Endpoint expose:                                                 │
│     GET /lakehouse/contracts/{code}/prefill?mode=single            │
│     GET /lakehouse/contracts/{code}/chart                          │
└────────────────────────────────────────────────────────────────────┘
                              ▲
                              │ HTTP qua nginx /lakehouse/*
                              │
┌──────────── TIER 2: DynamicForm Provider catalog (DB) ─────────────┐
│                                                                    │
│   DynamicFormDb.Providers                                          │
│     ┌──────────────────────────────────────────────────────────┐  │
│     │ Code=lakehouse  BaseUrl=http://lakehouseservice:8080     │  │
│     └──────────────────────────────────────────────────────────┘  │
│                                                                    │
│   DynamicFormDb.Operations                                         │
│     ┌─────────────────────────────────────────────────────────┐   │
│     │ lakehouse::prefill  Pattern=/lakehouse/contracts/{...}  │   │
│     │ lakehouse::chart    Pattern=/lakehouse/contracts/{...}  │   │
│     └─────────────────────────────────────────────────────────┘   │
│                                                                    │
│   FormScreen.DataSourcesJson                                       │
│     [{ Namespace: "finance", OperationId: "lakehouse::prefill",    │
│        Params: { contractCode: "finance.daily.row" } }]            │
│                                                                    │
│   FormField.DataBindingJson                                        │
│     { Expression: "{{sources.finance.invoiceDate}}" }              │
└────────────────────────────────────────────────────────────────────┘
                              ▲
                              │
                              │ HTTP qua nginx /forms/*
                              │
                       Admin / Designer UI / FE renderer
```

**Tier 1** đăng ký **tại code**: contract là 1 lớp C# implement `DataContract<TSchema>`, được register vào DI. Singleton in-memory `DataContractRegistry`.

**Tier 2** đăng ký **tại DB**: Provider + Operation là rows trong `DynamicFormDb`. Admin (hoặc migration seed) chèn vào.

Hai tier **không tự đồng bộ**. Khi thêm 1 contract mới ở Tier 1, phải thêm Operation tương ứng ở Tier 2 — hoặc dùng auto-sync (Phase 4 roadmap dưới).

---

## 3. Đã làm gì trong commit này

### 3.1 Sửa shape `FormPrefillResult` để FE bind 1 record

`src/BuildingBlocks/Contracts/DataContracts/FormPrefill/FormPrefillResult.cs`

```csharp
public sealed record FormPrefillResult(
    string ContractCode,
    int RowCount,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows)
{
    public IReadOnlyDictionary<string, object?>? Single { get; init; }
}
```

Thêm `Single` nullable. Backward compat: caller cũ nhận `rows: [...]`; caller mới (form) gọi `?mode=single` nhận thêm `single: {...}` phẳng.

### 3.2 Consumer trả `Single` khi `mode=single`

`src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/Consumers/FinanceDailyFormPrefillConsumer.cs`

```csharp
var single = string.Equals(query.Get("mode"), "single", StringComparison.OrdinalIgnoreCase) && rows.Count > 0
    ? rows[0]
    : null;

return new FormPrefillResult(ContractCode, rows.Count, rows) { Single = single };
```

### 3.3 Seed Provider `lakehouse` + 2 Operations

`src/Services/DynamicFormService/DynamicFormService.Infrastructure/Persistence/DynamicFormDbContext.cs`

`HasData()` cho `Provider` + `Operation` trong `OnModelCreating`. Dùng Guid + timestamp UTC cố định để migration deterministic (ai checkout repo, chạy migration cũng ra cùng SQL):

```
Provider:
  Id = 11111111-...   Code = lakehouse
  BaseUrl = http://lakehouseservice:8080

Operation 1:
  Id = 22222222-...   ProviderCode = lakehouse   OperationKey = prefill
  Pattern = /lakehouse/contracts/{contractCode}/prefill
  SchemaPath = /lakehouse/contracts/{contractCode}/schema  ← reserve Phase 2
  RequiredParams = ["contractCode"]

Operation 2:
  Id = 33333333-...   ProviderCode = lakehouse   OperationKey = chart
  Pattern = /lakehouse/contracts/{contractCode}/chart
  RequiredParams = ["contractCode"]
```

---

## 4. Step-by-step: admin tạo screen bind contract

### Bước 1 — Verify catalog đã có

```bash
# Provider
curl https://localhost:8443/forms/admin/providers \
  -H "Authorization: Bearer <token>"
# → có entry { code: "lakehouse", baseUrl: "http://lakehouseservice:8080", ... }

# Operations
curl https://localhost:8443/forms/admin/providers/lakehouse/operations \
  -H "Authorization: Bearer <token>"
# → có 2 entry: prefill, chart
```

Nếu trống → migration chưa apply. Force `dotnet ef database update` lên `DynamicFormDb`.

### Bước 2 — Tạo screen + gắn DataSource

```bash
# Tạo screen
curl -X POST https://localhost:8443/forms/admin/screens \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{
    "moduleCode": "finance",
    "code": "daily-report-view",
    "title": "Báo cáo tài chính ngày"
  }'

# Set DataSource: chọn lakehouse::prefill
curl -X PUT https://localhost:8443/forms/admin/screens/finance/daily-report-view/data-sources \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{
    "dataSources": [
      {
        "namespace": "finance",
        "operationId": "lakehouse::prefill",
        "requiredParams": ["contractCode"]
      }
    ]
  }'
```

> `namespace` = tên admin tự đặt, dùng trong expression. Không cần khớp `providerCode`.

### Bước 3 — Tạo field bind expression

Field trong `FormTemplate` (sẽ embed vào widget `FormSection` của screen):

```json
{
  "key": "invoiceDate",
  "label": "Ngày hoá đơn",
  "fieldType": "Text",
  "dataBinding": {
    "expression": "{{sources.finance.invoiceDate}}",
    "displayFormat": "date:DD/MM/YYYY"
  }
}
```

### Bước 4 — FE gọi layout endpoint

```bash
curl https://localhost:8443/forms/screens/finance/daily-report-view/layout \
  -H "Authorization: Bearer <token>"
```

Response (rút gọn):

```json
{
  "moduleCode": "finance",
  "code": "daily-report-view",
  "dataSources": [
    {
      "namespace": "finance",
      "serviceId": "lakehouse",
      "baseUrl": "http://lakehouseservice:8080",
      "resourcePath": "/lakehouse/contracts/{contractCode}/prefill",
      "kind": "Single",
      "operationId": "lakehouse::prefill",
      "requiredParams": ["contractCode"]
    }
  ],
  "tabs": [ ... ]
}
```

→ FE đã có đủ: `baseUrl + resourcePath`, biết params cần substitute (`contractCode`), biết kind (`Single` → tự append `?mode=single`).

### Bước 5 — FE fetch + bind

```ts
// Pseudo-code phía FE
const ds = layout.dataSources.find(d => d.namespace === "finance");
const url = `${ds.baseUrl}${ds.resourcePath
  .replace("{contractCode}", "finance.daily.row")
}?mode=single`;

const { single } = await fetch(url).then(r => r.json());
// single = { invoiceDate: "2026-06-09", departmentName: "...", ... }

// Eval expression {{sources.finance.invoiceDate}}
const value = single["invoiceDate"];   // "2026-06-09"
// Apply displayFormat "date:DD/MM/YYYY" → "09/06/2026"
```

---

## 5. Cheatsheet expression

| Expression | Resolve thành |
|------------|--------------|
| `{{sources.finance.invoiceDate}}` | `single["invoiceDate"]` từ contract `finance.daily.row` |
| `{{sources.finance.totalInvoiceAmount}}` | `single["totalInvoiceAmount"]` |
| `{{sources.patient.fullName}}` | `single["fullName"]` từ DataSource có `namespace=patient` |

Quy tắc:
- Phần thứ 2 (`finance`, `patient`) = `DataSource.Namespace` admin tự đặt.
- Phần thứ 3+ = key trong dict `single` (do consumer quyết định).
- `displayFormat` là **hint cho FE**, không phải eval BE. Format hỗ trợ: `date:DD/MM/YYYY`, `currency:VND`, v.v. (xem doc 35).

---

## 6. Vì sao trả `single` object thay vì `rows[0]`

Trước fix: FE phải biết quy ước "lấy index 0 của array" → expression `{{sources.finance.invoiceDate}}` không có chỗ chứa `[0]`.

Sau fix: BE trả thẳng object phẳng khi `mode=single`. FE eval expression như object lookup bình thường. Không trộn 2 layer concept (array iteration + object access).

Multi-row use case (vd: bảng) vẫn dùng default mode (không có `mode=single`), nhận `rows: [...]` như cũ. Hoàn toàn backward compat.

---

## 7. Roadmap — 4 phase, mỗi phase tự deliver value

| Phase | Tên | Tình trạng |
|-------|-----|-----------|
| **1** | Catalog-as-Code + Single-row fix | ✅ **DONE** (doc 58) |
| 2 | Schema discovery endpoint `/lakehouse/contracts/{code}/schema` (reflection) | Chưa |
| 3 | Auto-form generator: mở rộng `GenerateFromSourceCommand` hỗ trợ source type `datacontract` | Chưa |
| **4** | Auto-sync registry: `IHostedService` ở Lakehouse → gRPC sang DynamicForm tự tạo Provider/Operation | ✅ **DONE** (doc 59) |

### Phase 2 — Schema discovery (~1h)
Endpoint mới ở Lakehouse trả danh sách field bằng reflection trên `contract.SchemaType`:

```json
GET /lakehouse/contracts/finance.daily.row/schema
→ [
    { "name": "InvoiceDate", "jsonName": "invoiceDate", "csharpType": "DateOnly", "isOptional": false },
    { "name": "DepartmentName", "jsonName": "departmentName", "csharpType": "String", "isOptional": false },
    ...
  ]
```

Dùng cho admin UI dropdown "chọn field để bind". Operation `prefill` đã reserve `SchemaPath` trỏ tới endpoint này.

### Phase 3 — Auto-form generator (~5h)
Mở rộng `GenerateFromSourceCommand` (hiện đã hỗ trợ DataMatching) thêm source type `datacontract`. Admin chọn contract → BE đọc schema → sinh Form + Fields + DataSource + bindings + publish.

### Phase 4 — Auto-sync (DONE)
Xem **doc 59**. `IHostedService` ở Lakehouse khi startup gọi rpc `SyncRegistry` sang DynamicForm → upsert Provider `lakehouse` + Operations idempotent. Bỏ hẳn `HasData()` seed của doc 58 (migration `RemoveLakehouseSeed`). Thêm contract mới hoặc đổi `BaseUrl` Lakehouse không còn cần migration ở DynamicForm.

---

## 8. Troubleshooting

### Provider catalog trống
- Kiểm tra `DynamicFormDb.Providers` có row `lakehouse` chưa.
- Nếu chưa: chạy `dotnet ef database update` lên project `DynamicFormService.Infrastructure`.
- Migration phải có `InsertData("Providers", ...)` — review trong file `Migrations/<ts>_*.cs`.

### Resolve trả `BaseUrl = null`
- `GetScreenLayoutQuery` fallback null khi provider hoặc operation **không tồn tại / inactive**.
- Check `Status` của Provider `lakehouse` và Operation `prefill` trong DB — phải là `"Active"`.

### Expression `{{sources.finance.invoiceDate}}` không thay được giá trị
- Confirm `?mode=single` đã được append vào URL FE gọi. Default không có → `single` sẽ null.
- Check `DataSource.Namespace` của screen có khớp `finance` không (đoạn `sources.<namespace>.<field>`).
- Check key trong `single` object — chữ thường (`invoiceDate`) hay PascalCase (`InvoiceDate`)? Consumer hiện trả camelCase (xem `FinanceDailyFormPrefillConsumer.cs:32-42`).

### Contract mới chưa có Operation
- Hiện chưa có auto-sync. Phải thêm thủ công: hoặc viết migration `HasData()` mới, hoặc gọi `POST /forms/admin/providers/lakehouse/operations` qua admin API.
- Sau Phase 4 → tự động.

---

## 9. Files thay đổi trong commit này

| File | Loại |
|------|------|
| `src/BuildingBlocks/Contracts/DataContracts/FormPrefill/FormPrefillResult.cs` | Sửa — thêm `Single` property |
| `src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/Consumers/FinanceDailyFormPrefillConsumer.cs` | Sửa — populate `Single` khi `mode=single` |
| `src/Services/DynamicFormService/DynamicFormService.Infrastructure/Persistence/DynamicFormDbContext.cs` | Sửa — `SeedLakehouseProvider()` trong `OnModelCreating` |
| `docs/58-lakehouse-dynamicform-integration.md` | Mới — doc này |

Không đụng vào DataContract engine core, domain entity, hay endpoint handler nào — chỉ data seed + shape adjustment.
