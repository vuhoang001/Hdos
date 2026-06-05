# 41 — Loose Coupling Architecture (Provider Catalog)

> Thiết kế cho phép **DataMatchingService**, **LakehouseService**, **DynamicFormService** và **Frontend** kết hợp với nhau theo cách **tách bạch hoàn toàn**: thay đổi một service không kéo theo sửa code service khác. Mở rộng từ [40 — Schema Discovery](./40-schema-discovery.md).

---

## Mục lục

1. [Mục tiêu](#1-mục-tiêu)
2. [Vấn đề coupling hiện tại](#2-vấn-đề-coupling-hiện-tại)
3. [Kiến trúc 3 tầng tách bạch](#3-kiến-trúc-3-tầng-tách-bạch)
4. [Domain Model — Provider & Operation](#4-domain-model)
5. [DataSource mở rộng (backward compat)](#5-datasource-mở-rộng)
6. [Resolve Pipeline](#6-resolve-pipeline)
7. [API Spec](#7-api-spec)
8. [End-to-end Flow](#8-end-to-end-flow)
9. [Frontend impact](#9-frontend-impact)
10. [Migration Path](#10-migration-path)
11. [Convention & Naming](#11-convention--naming)
12. [Implementation Plan](#12-implementation-plan)

---

## 1. Mục tiêu

Ba nguyên tắc kiến trúc:

| # | Nguyên tắc | Hệ quả |
|---|------------|--------|
| 1 | **Producer không biết Consumer** | DataMatching/Lakehouse không reference DynamicForm. Service mới thêm vào không cần sửa code DynamicForm. |
| 2 | **Consumer không hardcode URL Producer** | DynamicForm không có chuỗi `/dm/records?...` nào trong code. Đổi endpoint Producer → sửa 1 dòng config. |
| 3 | **Frontend không hardcode mapping `serviceId → URL`** | FE đọc `baseUrl` từ layout response. Đổi infra (Producer chuyển host) → FE không build lại. |

---

## 2. Vấn đề coupling hiện tại

### 2.1. Trước khi có Provider Catalog

```
[Admin]                                   [Producer]
  │  PUT /forms/admin/screens/.../data-sources
  │     resourcePath: "/dm/records?sourceSystem=his-01&recordType=benh-nhan&field=MaBN&value={maBN}"
  │     ↑ admin phải biết và gõ tay URL của DataMatching
  │
  ▼
[DynamicForm DB]
  DataSourcesJson lưu thô URL
       │
       ▼
[FE runtime]
  SERVICE_MAP = { "datamatch": "https://localhost:8443", ... }
                  ↑ hardcoded trong FE source, mỗi env một bản

  fetch(SERVICE_MAP[ds.serviceId] + ds.resourcePath)
```

### 2.2. Hai chỗ coupling còn lại

| Chỗ | Vấn đề | Hậu quả |
|-----|--------|---------|
| URL gõ tay trong `resourcePath` | DataMatching đổi route `/dm/records` → `/dm/v2/records` | Tất cả screen đang dùng phải sửa từng cái |
| `SERVICE_MAP` hardcode bên FE | Thêm service mới (M02Service) | Phải rebuild + deploy lại FE |

---

## 3. Kiến trúc 3 tầng tách bạch

```
┌──────────────────────────────────────────────────────────────────────┐
│  Tầng 1 — PRODUCER (data services)                                   │
│  ┌─────────────────────┐  ┌─────────────────────┐  ┌──────────────┐ │
│  │ DataMatchingService │  │ LakehouseService    │  │ M01, M02 ... │ │
│  │   /dm/records       │  │ /lakehouse/snapshots│  │   ...        │ │
│  │   /dm/.../schema    │  │ /lakehouse/.../schema│ │              │ │
│  └─────────────────────┘  └─────────────────────┘  └──────────────┘ │
│  ▲ KHÔNG biết DynamicForm tồn tại                                    │
│  ▲ Chỉ expose REST + schema endpoint (contract chung — xem doc 40)   │
└──────────────────────────────────────────────────────────────────────┘
                              ▲
                              │  Admin đăng ký 1 lần qua catalog
                              │
┌──────────────────────────────────────────────────────────────────────┐
│  Tầng 2 — CATALOG (DynamicFormService)                               │
│                                                                      │
│  ┌──────────────────────┐    ┌──────────────────────────────────┐    │
│  │  Providers           │    │  Operations                      │    │
│  ├──────────────────────┤    ├──────────────────────────────────┤    │
│  │ id: "datamatch"      │◄───│ providerId, id: "patient-by-mabn"│    │
│  │ baseUrl: ".../dm"    │    │ pattern: "/records?...{maBN}"    │    │
│  │ status               │    │ schemaPath, requiredParams, kind │    │
│  └──────────────────────┘    └──────────────────────────────────┘    │
│                                                                      │
│  Vai trò: ghi nhận "tôi biết những Producer nào + Operations gì".    │
│  Producer URL/route đổi → sửa 1 dòng Operations, screen tự cập nhật. │
└──────────────────────────────────────────────────────────────────────┘
                              ▲
                              │  Admin chọn Operation từ dropdown
                              │
┌──────────────────────────────────────────────────────────────────────┐
│  Tầng 3 — CONSUMER (Screen + Frontend)                               │
│                                                                      │
│  Screen.DataSource = { namespace, operationId: "datamatch::xxx" }    │
│  └─ FE nhận layout với operationId đã resolve thành baseUrl+pattern │
│                                                                      │
│  FE chỉ fetch URL đã có sẵn, KHÔNG hardcode service nào               │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 4. Domain Model

### 4.1. `Provider` (Aggregate Root)

```csharp
public sealed class Provider : AggregateRoot<string>
{
    public string         Id           { get; private set; }  // slug, PK
    public string         DisplayName  { get; private set; }
    public string         BaseUrl      { get; private set; }  // "/dm" hoặc "https://datamatch.local"
    public ProviderStatus Status       { get; private set; }
    public DateTime       CreatedAtUtc { get; private set; }
    public DateTime?      UpdatedAtUtc { get; private set; }

    public static Provider Create(string id, string displayName, string baseUrl);
    public void Update(string displayName, string baseUrl);
    public void Activate();
    public void Deactivate();
}
```

| Field | Convention |
|-------|-----------|
| `Id` | slug `[a-z0-9-]+`, 2-30 ký tự. VD: `"datamatch"`, `"lakehouse"`, `"m01"` |
| `BaseUrl` | Path tương đối qua gateway (`"/dm"`) hoặc URL tuyệt đối. Không kết thúc bằng `/`. |
| `Status` | `Active` (visible cho admin chọn) \| `Inactive` (ẩn nhưng giữ data) |

### 4.2. `Operation` (Aggregate Root)

```csharp
public sealed class Operation : AggregateRoot<Guid>
{
    public Guid          Id             { get; private set; }
    public string        ProviderId     { get; private set; }    // FK logic, không cứng
    public string        OperationKey   { get; private set; }    // slug, unique trong provider
    public string        DisplayName    { get; private set; }
    public string        Pattern        { get; private set; }    // "/records?...{maBN}"
    public string?       SchemaPath     { get; private set; }    // "/sources/his-01/benh-nhan/schema"
    public string        RequiredParamsJson { get; private set; } // JSON array of string
    public OperationKind Kind           { get; private set; }    // Single | List
    public OperationStatus Status       { get; private set; }
    public DateTime      CreatedAtUtc   { get; private set; }
    public DateTime?     UpdatedAtUtc   { get; private set; }
}

public enum OperationKind  { Single = 0, List = 1 }
public enum OperationStatus { Active = 0, Inactive = 1 }
```

| Field | Convention |
|-------|-----------|
| `OperationKey` | slug `[a-z0-9-]+`. Unique trong cùng provider. VD: `"patient-by-mabn"` |
| `Pattern` | Path RELATIVE so với provider.BaseUrl. Có `{param}` placeholder. |
| `Kind` | `Single` (response.data là object) \| `List` (response.data là array) — FE biết cách parse |

**Khóa duy nhất:** `(ProviderId, OperationKey)`. Khi ref từ DataSource dùng dạng combined `"datamatch::patient-by-mabn"`.

---

## 5. DataSource mở rộng

Mở rộng `DataSource` value object trong DynamicFormService:

```csharp
public sealed record DataSource(
    string       Namespace,
    string?      OperationId,        // ← MỚI: combined "providerId::operationKey"
    string?      ServiceId,          // ← LEGACY: backward compat (doc 35)
    string?      ResourcePath,       // ← LEGACY
    string?      SchemaPath,         // ← LEGACY (doc 40)
    List<string> RequiredParams);
```

**Quy tắc resolve:**
1. Nếu `OperationId` có giá trị → resolve qua Provider Catalog (managed mode)
2. Nếu `OperationId` null nhưng `ResourcePath` có → dùng trực tiếp (legacy mode)
3. Cả hai null → invalid

**Lưu trữ:** vẫn JSON trong `FormScreens.DataSourcesJson`. Không cần migration phá structure cũ.

---

## 6. Resolve Pipeline

`GetScreenLayoutQuery` resolve `operationId` trước khi trả về:

```
GET /forms/screens/{moduleCode}/{screenCode}/layout
        │
        ▼
┌───────────────────────────────────────────────────────┐
│ GetScreenLayoutQueryHandler                            │
│                                                        │
│ foreach ds in screen.DataSources:                      │
│   if ds.OperationId is null:                           │
│      → emit DataSourceDto từ legacy fields             │
│   else:                                                │
│      (provId, opKey) = ds.OperationId.split("::")      │
│      prov = providerRepo.Get(provId)                   │
│      op   = operationRepo.Get(provId, opKey)           │
│                                                        │
│      if prov.Inactive OR op.Inactive:                  │
│         → emit với warning flag, FE skip               │
│      else:                                             │
│         → emit DataSourceDto:                          │
│            {                                           │
│              namespace,                                │
│              serviceId:      prov.Id,                  │
│              baseUrl:        prov.BaseUrl,    ← MỚI    │
│              resourcePath:   op.Pattern,               │
│              schemaPath:     op.SchemaPath,            │
│              requiredParams: op.RequiredParams,        │
│              kind:           op.Kind           ← MỚI    │
│            }                                           │
└───────────────────────────────────────────────────────┘
```

**FE nhận được:** layout với URL đầy đủ đã resolve. FE không cần biết Provider Catalog tồn tại — chỉ fetch `baseUrl + resourcePath`.

---

## 7. API Spec

### 7.1. Providers CRUD

```
POST   /forms/admin/providers
       Body: { id, displayName, baseUrl }
       
GET    /forms/admin/providers
       Query: ?status=active|inactive

PUT    /forms/admin/providers/{id}
       Body: { displayName, baseUrl }

DELETE /forms/admin/providers/{id}
       Conflict (409) nếu còn Operation tham chiếu — phải xóa Operations trước.
```

### 7.2. Operations CRUD (nested under provider)

```
POST   /forms/admin/providers/{providerId}/operations
       Body: { operationKey, displayName, pattern, schemaPath?, requiredParams[], kind }

GET    /forms/admin/providers/{providerId}/operations

PUT    /forms/admin/providers/{providerId}/operations/{operationKey}

DELETE /forms/admin/providers/{providerId}/operations/{operationKey}
       Conflict (409) nếu còn DataSource đang ref — phải sửa screen trước, hoặc dùng force=true.
```

### 7.3. Flat list cho dropdown FE

```
GET /forms/admin/operations
    → Cross-provider list, mỗi entry kèm providerId + providerDisplayName.
    → FE dùng để render ProviderOperationSelect (2 dropdown).
```

### 7.4. DataSource tham chiếu Operation

```
PUT /forms/admin/screens/{moduleCode}/{screenCode}/data-sources
[
  {
    "namespace": "benhnhan",
    "operationId": "datamatch::patient-by-mabn"
  },
  {
    "namespace": "xetnghiem",
    "operationId": "lakehouse::lab-result"
  }
]
```

Hoặc legacy mode (vẫn được hỗ trợ):
```json
{
  "namespace": "benhnhan",
  "serviceId": "datamatch",
  "resourcePath": "/dm/records?...&value={maBN}",
  "schemaPath":   "/dm/sources/his-01/benh-nhan/schema",
  "requiredParams": ["maBN"]
}
```

---

## 8. End-to-end Flow

```
[SETUP — 1 lần khi triển khai service mới]

1. Admin đăng ký Provider:
   POST /forms/admin/providers
   { id: "datamatch", displayName: "DataMatching", baseUrl: "/dm" }

2. Admin đăng ký Operations:
   POST /forms/admin/providers/datamatch/operations
   {
     operationKey:   "patient-by-mabn",
     displayName:    "Tìm bệnh nhân theo mã BN",
     pattern:        "/records?sourceSystem=his-01&recordType=benh-nhan&field=MaBN&value={maBN}",
     schemaPath:     "/sources/his-01/benh-nhan/schema",
     requiredParams: ["maBN"],
     kind:           "Single"
   }

[DESIGN — mỗi screen]

3. Admin tạo Screen + chọn Operation từ dropdown:
   PUT /forms/admin/screens/kham-benh/tiep-nhan/data-sources
   [{ namespace: "benhnhan", operationId: "datamatch::patient-by-mabn" }]

4. Admin tạo FormField. Có 2 cách binding:
   a. Tường minh: dropdown chọn field từ schema → expression
      "{{sources.benhnhan.fullName}}"
   b. Auto-mapping: đặt FormField.key="fullName" → runtime tự bind (doc 40)

[RENDER — mỗi lần user mở screen]

5. FE: GET /forms/screens/kham-benh/tiep-nhan/layout
   → Response có dataSources đã resolve:
      {
        namespace:      "benhnhan",
        serviceId:      "datamatch",
        baseUrl:        "/dm",
        resourcePath:   "/records?...&value={maBN}",
        schemaPath:     "/sources/his-01/benh-nhan/schema",
        requiredParams: ["maBN"],
        kind:           "Single"
      }

6. FE: replace {maBN} từ route, fetch baseUrl+resourcePath → render
   FE không hardcode bất kỳ URL nào.
```

---

## 9. Frontend impact

### 9.1. Không còn `SERVICE_MAP` hardcode

```typescript
// TRƯỚC — phải hardcode
const SERVICE_MAP = {
  datamatch: "https://localhost:8443",
  lakehouse: "https://localhost:8443",
};

// SAU — không cần map nữa
async function fetchSource(ds: DataSourceDto, params: Record<string,string>) {
  const path = ds.resourcePath.replace(/\{(\w+)\}/g, (_, k) => params[k]);
  const url  = ds.baseUrl + path;     // ← baseUrl từ BE
  return fetch(url).then(r => r.json());
}
```

### 9.2. Admin UI — 2 dropdown

```
┌──────────────────────────────────────────┐
│  Data Source                             │
│  Provider:  [DataMatching       ▼]       │  ← GET /forms/admin/providers
│  Operation: [Tìm bệnh nhân...   ▼]       │  ← GET /forms/admin/providers/{id}/operations
│  Namespace: [benhnhan            ]       │  ← admin tự đặt cho expression
└──────────────────────────────────────────┘
                ↓ Lưu xuống
{ namespace: "benhnhan", operationId: "datamatch::patient-by-mabn" }
```

---

## 10. Migration Path

```
Giai đoạn 1 — TRIỂN KHAI (không phá cái cũ)
  - Tạo bảng Providers + Operations (rỗng)
  - Thêm DataSource.OperationId optional, code resolve cả 2 mode
  - DataSource cũ (legacy) vẫn chạy nguyên

Giai đoạn 2 — SEED CATALOG
  - Script đọc tất cả DataSource hiện có
  - Sinh Provider + Operation tương ứng (1 operation per unique resourcePath)
  - Cập nhật DataSource cũ → set operationId, xóa resourcePath/schemaPath
  - Verify: screen render không đổi gì

Giai đoạn 3 — DEPRECATE LEGACY
  - Đánh dấu `[Obsolete]` cho path legacy
  - Validator cấm tạo DataSource mới với legacy mode
  - Sau N tháng: xóa code legacy
```

---

## 11. Convention & Naming

| Object | Naming | Ví dụ |
|--------|--------|-------|
| `Provider.Id` | slug `[a-z0-9-]+`, 2-30 ký tự | `datamatch`, `lakehouse`, `m01` |
| `Operation.OperationKey` | slug `[a-z0-9-]+`, 2-50 ký tự | `patient-by-mabn`, `lab-result-latest` |
| Combined ref | `<providerId>::<operationKey>` | `datamatch::patient-by-mabn` |
| `BaseUrl` | path/URL không có trailing `/` | `/dm`, `https://datamatch.local` |
| `Pattern` | bắt đầu bằng `/`, có `{param}` | `/records?value={maBN}` |
| `RequiredParams` | tên param trong `Pattern` (không `{}`) | `["maBN"]` |
| `Namespace` (DataSource) | `[a-z][a-z0-9_]*`, unique trong screen | `benhnhan`, `lab_result` |

---

## 12. Implementation Plan

| Bước | Module | Files chính | Test |
|------|--------|-------------|------|
| 1 | Domain | `Provider.cs`, `Operation.cs`, enums, `IProviderRepository`, `IOperationRepository` | unit test entity |
| 2 | Infrastructure | EF config × 2, migration mới (2 bảng) | apply migration |
| 3 | Application | Create/Update/Delete/List × 2 = 8 commands/queries | unit test handler |
| 4 | API | `AdminProvidersController`, `AdminOperationsController`, `OperationsListController` | curl |
| 5 | DataSource VO | Thêm `OperationId` optional, xử lý JSON deserialize | — |
| 6 | Layout resolve | Update `GetScreenLayoutQueryHandler` 2 nhánh (managed vs legacy) | render test |
| 7 | SetDataSources validator | Cho phép `OperationId` HOẶC `ResourcePath`, không cả hai null | — |
| 8 | Seed script | One-off migration từ legacy DataSource → catalog | run trên dev |
| 9 | Docs | Doc 41 (this), update 35 + 40 reference | — |

**Ước lượng:** ~20 file mới + ~5 file sửa. Không phá backward compat.

---

## 13. Tham chiếu

- [25 — Server-Driven UI](./25-sdui-server-driven-ui.md) — Triết lý SDUI
- [29 — DynamicFormService](./29-dynamic-form-service.md) — Tổng quan service
- [35 — Expression Data Binding](./35-expression-data-binding.md) — Cú pháp `{{sources.x.y}}`
- [38 — Frontend SDUI Implementation Guide](./38-frontend-sdui-implementation-guide.md) — FE side
- [40 — Schema Discovery](./40-schema-discovery.md) — Contract `/.../schema`
