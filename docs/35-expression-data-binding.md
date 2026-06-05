# 35 — Expression Data Binding (Approach 2)

## Tổng quan

DynamicFormService hỗ trợ **Expression Language Engine** cho phép field trong form tự động lấy giá trị từ data của các service khác (M01, OrderService, v.v.) mà không cần coupling giữa các service.

Cơ chế gồm 2 phần:
1. **DataSources** — khai báo tại cấp Screen: service nào cần gọi, endpoint nào, param gì
2. **DataBinding expression** — khai báo tại cấp Field: lấy giá trị từ source nào bằng cú pháp `{{sources.namespace.path}}`

---

## Cách hoạt động

```
[Admin config — 1 lần]
Screen "patient-admission" → dataSources:
  - namespace: "patient", serviceId: "m01", resourcePath: "/m01/patients/{patientId}"

Field "Họ tên" → dataBinding.expression: "{{sources.patient.fullName}}", isReadOnly: true

[Frontend runtime — mỗi lần mở form]
1. GET /forms/screens/m01/patient-admission/layout
   ← trả về dataSources manifest + fields với expression
2. Frontend đọc dataSources, extract params từ route context
3. Fetch song song các sources:
   GET /m01/patients/123 → { fullName: "Nguyễn Văn A", dob: "1990-01-01" }
4. Evaluate expression: "{{sources.patient.fullName}}" → "Nguyễn Văn A"
5. Render form đã pre-fill, field readOnly bị khóa
6. User điền các field tự do → POST .../submit
```

---

## Cú pháp Expression

| Expression | Ý nghĩa |
|---|---|
| `{{sources.patient.fullName}}` | Field `fullName` từ source `patient` |
| `{{sources.visit.details.admissionDate}}` | Nested path |
| `{{sources.order.items[0].name}}` | Array index (frontend tự xử lý) |

`DisplayFormat` là hint cho frontend renderer:

| Giá trị | Ý nghĩa |
|---|---|
| `date:DD/MM/YYYY` | Format ngày tháng |
| `currency:VND` | Format tiền tệ |
| `null` | Hiển thị nguyên gốc |

---

## API

### Khai báo DataSources cho Screen

```
PUT /forms/admin/screens/{moduleCode}/{screenCode}/data-sources
Authorization: Bearer <token>
Content-Type: application/json

[
  {
    "namespace": "patient",
    "serviceId": "m01",
    "resourcePath": "/m01/patients/{patientId}",
    "requiredParams": ["patientId"]
  },
  {
    "namespace": "visit",
    "serviceId": "m01",
    "resourcePath": "/m01/visits/{visitId}",
    "requiredParams": ["visitId"]
  }
]
```

- Gọi lại để thay thế toàn bộ (full replacement).
- Gửi `[]` để xóa hết data sources của screen.

### Thêm field có binding

```
POST /forms/admin/forms/{formTemplateId}/fields
Content-Type: application/json

{
  "key": "patient_name",
  "label": "Họ tên bệnh nhân",
  "fieldType": "Text",
  "order": 1,
  "required": false,
  "width": "Full",
  "dataBindingExpression": "{{sources.patient.fullName}}",
  "displayFormat": null,
  "isReadOnly": true
}
```

### Đọc layout (public — frontend dùng)

```
GET /forms/screens/{moduleCode}/{screenCode}/layout

Response:
{
  "id": "...",
  "moduleCode": "m01",
  "code": "patient-admission",
  "title": "Phiếu nhập viện",
  "dataSources": [
    {
      "namespace": "patient",
      "serviceId": "m01",
      "resourcePath": "/m01/patients/{patientId}",
      "requiredParams": ["patientId"]
    }
  ],
  "tabs": [
    {
      "widgets": [
        {
          "widgetType": "FormSection",
          "formSchema": {
            "fields": [
              {
                "key": "patient_name",
                "label": "Họ tên bệnh nhân",
                "dataBinding": {
                  "expression": "{{sources.patient.fullName}}",
                  "displayFormat": null
                },
                "isReadOnly": true
              }
            ]
          }
        }
      ]
    }
  ]
}
```

---

## Hướng dẫn Frontend

### Bước 1 — Fetch layout

```typescript
const layout = await fetch(`/forms/screens/${moduleCode}/${screenCode}/layout`)
  .then(r => r.json());
```

### Bước 2 — Extract params từ route và fetch sources song song

```typescript
// Params từ URL hiện tại, ví dụ: /patients/123/visits/456/admission
const routeParams = { patientId: '123', visitId: '456' };

const sourceData: Record<string, any> = {};

await Promise.all(
  layout.dataSources.map(async (source) => {
    // Substitute {param} trong resourcePath
    const url = source.resourcePath.replace(
      /\{(\w+)\}/g,
      (_, key) => routeParams[key] ?? ''
    );
    const data = await fetch(url).then(r => r.json());
    sourceData[source.namespace] = data;
  })
);
```

### Bước 3 — Evaluate expression

```typescript
function evaluateExpression(expression: string, sources: Record<string, any>): string | null {
  // Match {{sources.namespace.path.to.field}}
  const match = expression.match(/^\{\{sources\.(\w+)\.(.+)\}\}$/);
  if (!match) return null;

  const [, namespace, path] = match;
  const sourceObj = sources[namespace];
  if (!sourceObj) return null;

  // Traverse dot-notation path
  return path.split('.').reduce((obj, key) => obj?.[key], sourceObj) ?? null;
}

// Dùng khi render mỗi field
function getFieldValue(field: FormField): string | null {
  if (!field.dataBinding) return null;
  return evaluateExpression(field.dataBinding.expression, sourceData);
}
```

### Bước 4 — Render field

```typescript
// Nếu field có dataBinding → pre-fill value
// Nếu field isReadOnly → disabled/display-only
// Nếu field có displayFormat → áp dụng formatter

function renderField(field: FormField) {
  const boundValue = getFieldValue(field);

  return (
    <input
      defaultValue={boundValue ?? ''}
      disabled={field.isReadOnly}
      placeholder={field.placeholder}
    />
  );
}
```

### Bước 5 — Submit (chỉ gửi field tự do)

```typescript
// Chỉ include field không isReadOnly vào payload submit
const answers = fields
  .filter(f => !f.isReadOnly)
  .map(f => ({ fieldKey: f.key, value: formValues[f.key] ?? null }));

await fetch(`/forms/${moduleCode}/${formKey}/submit`, {
  method: 'POST',
  body: JSON.stringify({ answers }),
});
```

---

## Quy tắc namespace

| Convention | Ví dụ |
|---|---|
| Lowercase, bắt đầu bằng chữ | `patient`, `visit`, `order` |
| Chỉ chứa `[a-z0-9_]` | `lab_result`, `admission2` |
| Unique trong cùng một screen | Không có 2 source cùng namespace |

---

## Schema DB

| Bảng | Cột mới | Kiểu |
|---|---|---|
| `FormFields` | `DataBindingJson` | `jsonb` nullable |
| `FormFields` | `IsReadOnly` | `boolean` default `false` |
| `FormScreens` | `DataSourcesJson` | `jsonb` nullable |

---

## Schema Discovery — UI dropdown thay vì gõ tay

Mỗi `DataSource` có thêm field optional `schemaPath` trỏ về endpoint trả danh sách field FE có thể bind:

```json
{
  "namespace": "benhnhan",
  "serviceId": "datamatch",
  "resourcePath": "/dm/records?...&value={maBN}",
  "schemaPath":   "/dm/sources/his-01/benh-nhan/schema",
  "requiredParams": ["maBN"]
}
```

FE dùng `schemaPath` để hiển thị dropdown khi admin config DataBinding — không phải gõ `{{sources.benhnhan.fullName}}` bằng tay.

Hỗ trợ thêm **auto-mapping by name** runtime: `FormField.key` trùng tên field trong schema → tự bind, không cần khai báo expression.

Xem chi tiết: [40 — Schema Discovery](./40-schema-discovery.md)

---

## Provider Catalog (loose coupling)

`DataSource` hiện hỗ trợ thêm field `operationId` (managed mode) thay vì gõ tay `serviceId` + `resourcePath`:

```json
{
  "namespace": "benhnhan",
  "operationId": "datamatch::patient-by-mabn",
  "requiredParams": ["maBN"]
}
```

Khi FE gọi `GET /forms/screens/.../layout`, BE tự resolve `operationId` qua Provider/Operation catalog → trả về `baseUrl + resourcePath + schemaPath + kind` đầy đủ. Đổi URL service ở Provider/Operation, mọi screen tự cập nhật. FE không còn hardcode mapping `serviceId → URL`.

Vẫn hỗ trợ legacy mode (gõ tay `serviceId` + `resourcePath`) cho backward compat.

Xem chi tiết: [41 — Loose Coupling Architecture](./41-loose-coupling-architecture.md)
