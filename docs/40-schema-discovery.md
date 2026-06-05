# 40 — Schema Discovery

> Cơ chế cho phép Frontend đọc **danh sách field có sẵn** của một DataSource, dùng để hiển thị dropdown khi admin config DataBinding — thay vì gõ tay expression `{{sources.xxx.field}}`.

---

## 1. Vấn đề

Trước khi có Schema Discovery, admin tạo `FormField` với DataBinding phải gõ tay:

```json
{ "dataBindingExpression": "{{sources.benhnhan.fullName}}" }
```

→ Không biết field `fullName` có thật trong source không. Sai chính tả → field hiển thị trống, không có error gì.

## 2. Giải pháp

Mỗi service đóng vai trò "DataSource Provider" expose endpoint `/.../schema` trả về danh sách field. Frontend đọc và đổ vào dropdown.

```
Admin chọn source        FE fetch schemaPath          FE hiển thị dropdown
"benhnhan"          →    GET /dm/.../schema      →    [fullName, dob, khoa, ...]
                                                       ↑ admin chọn 1 cái → sinh expression tự động
```

---

## 3. Contract chung — cả 2 service trả giống nhau

```json
{
  "success": true,
  "data": {
    "namespace": "his-01/benh-nhan",
    "businessKeyField": "maBN",
    "fields": [
      { "key": "fullName",  "type": "string", "label": "Họ tên",     "sourceField": "ho_ten" },
      { "key": "dob",       "type": "date",   "label": "Ngày sinh",  "sourceField": "ngay_sinh" },
      { "key": "hba1c",     "type": "number", "label": null,         "sourceField": null }
    ]
  }
}
```

| Field | Mô tả |
|-------|-------|
| `key` | Tên canonical field — dùng trong expression `{{sources.<ns>.<key>}}` |
| `type` | Một trong `"string"`, `"number"`, `"date"`, `"boolean"` |
| `label` | Tên hiển thị cho người dùng. `null` nếu nguồn không cung cấp |
| `sourceField` | Tên field gốc trước rename (DataMatching dùng). `null` cho Lakehouse |

---

## 4. Endpoint cụ thể

### 4.1. DataMatchingService

```
GET /dm/sources/{sourceSystem}/{recordType}/schema
```

Lấy danh sách canonical field từ `SourceProfile.Mappings` đã đăng ký. Mỗi entry `(sourceField → canonicalKey)` thành 1 field trong response. Type mặc định `"string"`.

**Ví dụ:**
```bash
curl https://localhost:8443/dm/sources/his-01/benh-nhan/schema
```

### 4.2. LakehouseService

```
GET /lakehouse/snapshots/schema?namespace={namespace}
```

Lấy snapshot **mới nhất** trong namespace → introspect top-level keys của `Payload` JSON. Type được infer:
- `boolean` từ JSON `true/false`
- `number` từ JSON number
- `date` từ JSON string khớp prefix ISO `YYYY-MM-DD`
- `string` cho phần còn lại

`sourceField` luôn `null` vì Lakehouse không có mapping.

**Ví dụ:**
```bash
curl "https://localhost:8443/lakehouse/snapshots/schema?namespace=lab-result"
```

---

## 5. DynamicFormService — DataSource.SchemaPath

`DataSource` value object thêm field `SchemaPath` optional:

```csharp
public sealed record DataSource(
    string       Namespace,
    string       ServiceId,
    string       ResourcePath,
    List<string> RequiredParams,
    string?      SchemaPath = null);
```

Lưu cùng JSON trong `FormScreens.DataSourcesJson`. **Không cần migration** — JSON cũ thiếu field tự deserialize thành `null`.

### 5.1. Khai báo qua Admin API

```bash
PUT /forms/admin/screens/{moduleCode}/{screenCode}/data-sources
[
  {
    "namespace": "benhnhan",
    "serviceId": "datamatch",
    "resourcePath": "/dm/records?sourceSystem=his-01&recordType=benh-nhan&field=MaBN&value={maBN}",
    "schemaPath":   "/dm/sources/his-01/benh-nhan/schema",
    "requiredParams": ["maBN"]
  },
  {
    "namespace": "xetnghiem",
    "serviceId": "lakehouse",
    "resourcePath": "/lakehouse/snapshots/latest?namespace=lab-result&key={maBN}",
    "schemaPath":   "/lakehouse/snapshots/schema?namespace=lab-result",
    "requiredParams": ["maBN"]
  }
]
```

**Khuyến nghị:** thay vì gõ `serviceId + resourcePath + schemaPath` tay cho mỗi screen, hãy đăng ký 1 lần vào Provider Catalog rồi dùng `operationId` (xem [doc 41](./41-loose-coupling-architecture.md)). Cách này tránh phải sửa nhiều screen khi URL service thay đổi.

### 5.2. Layout response trả về SchemaPath

```bash
GET /forms/screens/{moduleCode}/{screenCode}/layout
```

Mỗi entry trong `dataSources[]` có thêm field `schemaPath` — FE dùng để fetch schema khi mở dialog config DataBinding.

---

## 6. Frontend — UI Admin

Khi admin tạo/sửa `FormField`, dialog DataBinding dùng 2 dropdown thay vì text input:

```
┌──────────────────────────────────────────┐
│  Data Binding                            │
│  Source:  [benhnhan          ▼]          │  ← từ screen.dataSources[]
│  Field:   [fullName          ▼]          │  ← fetch schemaPath khi đổi Source
│  Preview: {{sources.benhnhan.fullName}}  │  ← tự sinh
│  ☑ Read-only                             │
│  Format:  [date:DD/MM/YYYY  ▼]           │
└──────────────────────────────────────────┘
```

**Flow:**
1. Đọc `screen.dataSources` → dropdown 1 (namespaces)
2. User chọn namespace → FE gọi `schemaPath` → đổ vào dropdown 2 (fields)
3. User chọn field → sinh expression `{{sources.<ns>.<key>}}`
4. Lưu xuống BE qua `POST/PUT .../fields`

**Fallback:** nếu DataSource không có `schemaPath` → dropdown 2 đổi thành text input (backward compat).

---

## 7. Frontend — Runtime auto-mapping by name

Mỗi lần render screen, FE đã fetch sẵn `sourceData` (xem [38-frontend-sdui-implementation-guide.md](./38-frontend-sdui-implementation-guide.md)). Với mỗi `FormField`:

```typescript
function resolveFieldBinding(field, dataSources, sourceData) {
  // 1. Field có dataBinding tường minh → dùng
  if (field.dataBinding) return evaluate(field.dataBinding.expression, sourceData);

  // 2. Auto-mapping: tìm namespace có field trùng key
  for (const ds of dataSources) {
    const ns = sourceData[ds.namespace];
    if (ns && field.key in ns) {
      return ns[field.key];   // dùng giá trị từ source đầu tiên trùng
    }
  }

  // 3. Không khớp → free input
  return null;
}
```

**Quy ước:** nếu `FormField.key` trùng với một field trong bất kỳ DataSource nào → tự bind. Trùng ở nhiều source → lấy source đầu tiên theo thứ tự trong `dataSources[]`.

**Ưu điểm:**
- Không phải khai báo binding cho từng field khi tên trùng
- Đổi schema source không cần re-publish form
- Backward compat: form cũ vẫn chạy đúng

---

## 8. End-to-end ví dụ

```
[Backend setup 1 lần]
1. DataMatching: POST /dm/sources    → đăng ký mapping his-01/benh-nhan
2. Lakehouse:    consume event      → snapshot lab-result được lưu
3. DynamicForm:  PUT  .../data-sources với schemaPath cho cả 2 nguồn

[Admin tạo form]
4. POST .../fields với key="fullName"   → auto bind từ benhnhan (runtime)
5. POST .../fields với key="hba1c"      → auto bind từ xetnghiem (runtime)
6. POST .../fields với key="ghi_chu"    → free input (không trùng)
7. PUT  .../publish

[User mở screen]
8. GET  /forms/screens/.../layout      → nhận layout + dataSources có schemaPath
9. FE fetch song song 2 dataSources    → có sourceData
10. FE map mỗi field → giá trị hoặc input trống
11. User điền các field free → POST .../submit
```

---

## 9. Files liên quan

| File | Vai trò |
|------|---------|
| `DataMatchingService/.../Sources/GetSchema/GetSourceSchemaQuery.cs` | Query handler `/dm/.../schema` |
| `LakehouseService/.../Snapshots/GetSchema/GetSnapshotSchemaQuery.cs` | Query handler `/lakehouse/.../schema` với type inference |
| `DynamicFormService/.../ValueObjects/DataSource.cs` | VO thêm `SchemaPath` |
| `DynamicFormService/.../Features/Screens/SetDataSources/SetScreenDataSourcesCommand.cs` | Nhận `SchemaPath` từ admin |
| `DynamicFormService/.../Features/Screens/GetScreenLayout/GetScreenLayoutQuery.cs` | Trả `SchemaPath` ra FE |

## 10. Tham chiếu

- [35 — Expression Data Binding](./35-expression-data-binding.md) — cơ chế `{{sources.x.y}}`
- [38 — Frontend SDUI Implementation Guide](./38-frontend-sdui-implementation-guide.md) — code FE fetch/evaluate
- [39 — LakehouseService](./39-lakehouse-service.md) — domain + snapshot ingest
- [41 — Loose Coupling Architecture](./41-loose-coupling-architecture.md) — mở rộng Schema Discovery thành Provider Catalog đầy đủ (FE không hardcode URL, admin chọn dropdown thay vì gõ resourcePath)
