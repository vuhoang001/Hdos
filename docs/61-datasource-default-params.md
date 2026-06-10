# 61 — DataSource `defaultParams`: lưu giá trị mặc định cho `{param}` placeholder

> Cho phép admin gán giá trị mặc định cho các `{param}` placeholder trong `resourcePath` ở screen-level data source. FE merge `defaultParams` với URL query params trước khi fetch — **URL params wins**.
>
> Companion: doc 41 (Loose Coupling), doc 58 (Catalog), doc 60 (FE Integration DataContract Prefill).

---

## 1. Vấn đề

`Operation.Pattern` trong catalog là **template** chứa placeholder, ví dụ:

```
/lakehouse/contracts/{contractCode}/prefill
```

Catalog cố ý không hard-code contract code để 1 operation phục vụ N contracts (xem `DataContractsRegistration.cs:52-68`). Hệ quả: mỗi lần screen render, FE phải biết giá trị `contractCode` để substitute placeholder.

Trước doc này, FE chỉ có 1 cách duy nhất: lấy từ **URL query string** (`?contractCode=patient.daily.new`). Vấn đề:

- Người dùng phải mở screen qua URL có sẵn param → URL dài, dễ sai.
- Không có "default" cho screen — mỗi lần copy link phải nhớ kèm param.
- Admin không lưu được lựa chọn của mình khi cấu hình data source.

---

## 2. Giải pháp

Thêm field `defaultParams: Dictionary<string, string>?` vào DataSource VO. Admin set qua FieldBrowser; FE merge với URL params (URL wins).

### Flow

```
┌─────────────┐                                                  ┌────────────┐
│   Admin     │  1. FieldBrowser: chọn contractCode = "patient.daily.new"
│   (FE)      │  ──────────────────────────────────────────────►  │ BE         │
│             │  PUT /screens/{m}/{c}/data-sources                │ DynamicForm│
│             │  body: { defaultParams: { contractCode: "..." }} │            │
└─────────────┘  ◄──────────────────────────────────────────────  └────────────┘
                 saved to FormScreen.DataSourcesJson (jsonb)

┌─────────────┐                                                  ┌────────────┐
│   End user  │  2. GET /forms/screens/{m}/{c}/layout            │ BE         │
│   (FE)      │  ──────────────────────────────────────────────►  │ DynamicForm│
│             │  ◄──────────────────────────────────────────────  │            │
│             │  ScreenLayoutDto.dataSources[*].defaultParams    └────────────┘
│             │
│             │  3. FE merge: { ...defaultParams, ...urlParams } (URL wins)
│             │     substitute {contractCode} → "patient.daily.new"
│             │     fetch /lakehouse/contracts/patient.daily.new/prefill
└─────────────┘
```

---

## 3. Backend changes

### 3.1. Domain VO

`src/Services/DynamicFormService/DynamicFormService.Domain/ValueObjects/DataSource.cs`:

```csharp
public sealed record DataSource(
    string       Namespace,
    string?      ServiceId,
    string?      ResourcePath,
    List<string> RequiredParams,
    string?      SchemaPath     = null,
    string?      OperationId    = null,
    Dictionary<string, string>? DefaultParams = null);  // ← MỚI
```

### 3.2. Persistence

**Không cần migration.** `FormScreen.DataSourcesJson` đã là `jsonb` (PostgreSQL); `JsonSerializer` tự serialize property mới vào cột hiện có.

Row cũ (chưa có `defaultParams`) deserialize ra `null` — backward-compat.

### 3.3. Command — admin lưu

`SetScreenDataSourcesCommand.DataSourceInput`:

```csharp
public sealed record DataSourceInput(
    string       Namespace,
    List<string> RequiredParams,
    string?      OperationId   = null,
    string?      ServiceId     = null,
    string?      ResourcePath  = null,
    string?      SchemaPath    = null,
    Dictionary<string, string>? DefaultParams = null);  // ← MỚI
```

Handler pass thẳng vào `new DataSource(...)`. Không có validator giới hạn size — để mở.

### 3.4. Query — FE đọc

`GetScreenLayoutQuery.ResolveDataSourceAsync` echo `d.DefaultParams` ở **cả 3 nhánh** return:

- LEGACY mode (không có OperationId)
- Fallback (OperationId malformed hoặc Provider/Operation không tồn tại)
- MANAGED mode (resolved thành công)

```csharp
return new DataSourceDto(
    Namespace:      d.Namespace,
    ServiceId:      provider.Code,
    ResourcePath:   operation.Pattern,
    RequiredParams: requiredParams,
    SchemaPath:     operation.SchemaPath,
    BaseUrl:        provider.BaseUrl,
    Kind:           operation.Kind.ToString(),
    OperationId:    operation.GetCombinedRef(),
    DefaultParams:  d.DefaultParams);  // ← MỚI (echo y nguyên)
```

### 3.5. DTO

`DynamicFormDtos.DataSourceDto` cũng có `DefaultParams` để serialize ra JSON cho FE.

---

## 4. API contract

### PUT `/forms/screens/{moduleCode}/{screenCode}/data-sources`

Request body — admin set default value cho placeholder:

```json
{
  "dataSources": [
    {
      "namespace": "patient",
      "operationId": "lakehouse::prefill",
      "requiredParams": ["contractCode"],
      "defaultParams": {
        "contractCode": "patient.daily.new"
      }
    }
  ]
}
```

### GET `/forms/screens/{moduleCode}/{screenCode}/layout`

Response — FE đọc để fetch:

```json
{
  "dataSources": [
    {
      "namespace": "patient",
      "serviceId": "lakehouse",
      "resourcePath": "/lakehouse/contracts/{contractCode}/prefill",
      "requiredParams": ["contractCode"],
      "schemaPath": "/lakehouse/contracts/{contractCode}/schema",
      "baseUrl": "http://lakehouseservice:8080",
      "kind": "Single",
      "operationId": "lakehouse::prefill",
      "defaultParams": {
        "contractCode": "patient.daily.new"
      }
    }
  ]
}
```

---

## 5. FE merge logic

```ts
// useDataSources.ts (đã implement bởi FE)
const resolvedParams = {
  ...source.defaultParams,    // BE-stored defaults
  ...urlParams,                // URL query string overrides
};

const resourcePath = substitute(source.resourcePath, resolvedParams);
// "/lakehouse/contracts/{contractCode}/prefill"
//   + { contractCode: "patient.daily.new" }
//   → "/lakehouse/contracts/patient.daily.new/prefill"

fetch(`${source.baseUrl}${resourcePath}`);
```

**Quy tắc URL wins**: nếu URL có `?contractCode=finance.daily.row`, giá trị này override `defaultParams.contractCode`. Cho phép share link với override mà không cần sửa data source config.

---

## 6. Migration path

- **Screen đã tồn tại**: row cũ không có `defaultParams` → `null` → FE rơi về URL params như trước. Không break.
- **Screen mới**: FieldBrowser auto-write `defaultParams` khi admin chọn contract → end user không cần URL param.

---

## 7. Files thay đổi (tóm tắt)

| File | Mục đích |
|---|---|
| `DynamicFormService.Domain/ValueObjects/DataSource.cs` | Thêm `DefaultParams` vào VO |
| `DynamicFormService.Application/Features/Screens/SetDataSources/SetScreenDataSourcesCommand.cs` | Accept + persist `DefaultParams` |
| `DynamicFormService.Application/DTOs/DynamicFormDtos.cs` | Expose `DefaultParams` trong response DTO |
| `DynamicFormService.Application/Features/Screens/GetScreenLayout/GetScreenLayoutQuery.cs` | Echo `DefaultParams` ở 3 nhánh return |

**Không sửa**: `FormScreenConfiguration.cs` (jsonb auto), không tạo migration, không động Lakehouse contracts catalog.

---

## 8. Related docs

- [41 — Loose Coupling Architecture](./41-loose-coupling-architecture.md)
- [42 — Admin API Refactor](./42-admin-api-refactor.md)
- [58 — Lakehouse ↔ DynamicForm Integration](./58-lakehouse-dynamicform-integration.md)
- [60 — FE Integration: DataContract Prefill](./60-fe-integration-datacontract-prefill.md)
