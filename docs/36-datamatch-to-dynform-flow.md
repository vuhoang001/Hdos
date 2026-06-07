# 36 — Full Flow: DataMatching → Auto-generate DynamicForm

## Tổng quan

Luồng này cho phép dữ liệu từ DataMatchingService (ingest + dedup + match) tự động được hiển thị trên một giao diện form động mà không cần frontend code mới.

```
[1] Đăng ký SourceProfile (1 lần)
         ↓
[2] Ingest dữ liệu (JSON hoặc file)
         ↓ MatchingWorker (≤30s)
[3] Record status = Matched
         ↓
[4] Auto-generate DynamicForm (1 API call)
    → Module + Screen + DataSources + Form + Fields (với expressions) + Tab + Widget
    → Published & sẵn sàng
         ↓
[5] Frontend mở form
    GET /forms/screens/{module}/{screen}/layout
    → Đọc dataSources → Fetch /dm/records/{recordId}
    → Evaluate {{sources.record.TenBenhNhan}} → "Nguyễn Văn A"
    → Render form đã pre-filled
         ↓
[6] User điền field tự do → Submit
```

---

## Demo HTTP Script

> Chạy bằng VS Code REST Client, IntelliJ HTTP Client, hoặc `curl`.
> Base URL: `http://localhost:5000`

```http
### ════════════════════════════════════════════════════════════
### PHẦN 1 — DATAMATCHINGSERVICE: Ingest dữ liệu
### ════════════════════════════════════════════════════════════

### Bước 1.1 — Đăng ký SourceProfile (ánh xạ field raw → canonical)
POST http://localhost:5000/dm/sources
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "recordType": "benh-nhan",
  "displayName": "Bệnh nhân nội trú (HIS-01)",
  "businessKeyField": "MaBenhNhan",
  "mappings": {
    "ho_ten":        "TenBenhNhan",
    "ngay_sinh":     "NgaySinh",
    "ma_benh_nhan":  "MaBenhNhan",
    "khoa_dieu_tri": "KhoaDieuTri",
    "chan_doan":      "ChanDoan",
    "ngay_nhap_vien":"NgayNhapVien"
  }
}

### Bước 1.2 — Ingest bệnh nhân #1
POST http://localhost:5000/dm/ingest/json
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "recordType": "benh-nhan",
  "payload": {
    "ho_ten": "Nguyễn Văn An",
    "ngay_sinh": "1985-03-15",
    "ma_benh_nhan": "BN2024001",
    "khoa_dieu_tri": "Khoa Tim Mạch",
    "chan_doan": "Tăng huyết áp độ II",
    "ngay_nhap_vien": "2026-06-01"
  }
}
# → Lưu kết quả: { "id": "<record-id>", "status": "Pending" }
# → Sau ~30s MatchingWorker chạy → status = "Matched"

### Bước 1.3 — Ingest bệnh nhân #2
POST http://localhost:5000/dm/ingest/json
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "recordType": "benh-nhan",
  "payload": {
    "ho_ten": "Trần Thị Bình",
    "ngay_sinh": "1992-07-20",
    "ma_benh_nhan": "BN2024002",
    "khoa_dieu_tri": "Khoa Nội Tổng Hợp",
    "chan_doan": "Đái tháo đường type 2",
    "ngay_nhap_vien": "2026-06-02"
  }
}

### Bước 1.4 — Kiểm tra record đã matched chưa (đợi ~30s)
GET http://localhost:5000/dm/records?sourceSystem=his-01&recordType=benh-nhan

### Bước 1.5 — Lấy record cụ thể theo ID (thay <record-id> bằng id từ bước 1.2)
GET http://localhost:5000/dm/records/<record-id>
# → Response: { canonicalPayload: { TenBenhNhan: "Nguyễn Văn An", ... } }


### ════════════════════════════════════════════════════════════
### PHẦN 2 — DYNAMICFORMSERVICE: Auto-generate giao diện
### ════════════════════════════════════════════════════════════

### Bước 2.1 — Auto-generate toàn bộ screen + form chỉ với 1 call
POST http://localhost:5000/forms/admin/generate-from-source
Content-Type: application/json

{
  "moduleCode": "datamatch",
  "moduleName": "DataMatching — Xét duyệt hồ sơ",
  "screenCode": "patient-review",
  "screenTitle": "Xét duyệt hồ sơ bệnh nhân",
  "formKey": "patient-data",
  "formTitle": "Thông tin bệnh nhân",
  "dataSource": {
    "namespace": "record",
    "serviceId": "datamatch",
    "resourcePath": "/dm/records/{recordId}",
    "requiredParams": ["recordId"]
  },
  "fields": [
    {
      "canonicalKey": "MaBenhNhan",
      "label": "Mã bệnh nhân",
      "fieldType": "Text"
    },
    {
      "canonicalKey": "TenBenhNhan",
      "label": "Họ tên bệnh nhân",
      "fieldType": "Text"
    },
    {
      "canonicalKey": "NgaySinh",
      "label": "Ngày sinh",
      "fieldType": "Date",
      "displayFormat": "date:DD/MM/YYYY"
    },
    {
      "canonicalKey": "KhoaDieuTri",
      "label": "Khoa điều trị",
      "fieldType": "Text"
    },
    {
      "canonicalKey": "ChanDoan",
      "label": "Chẩn đoán",
      "fieldType": "Text"
    },
    {
      "canonicalKey": "NgayNhapVien",
      "label": "Ngày nhập viện",
      "fieldType": "Date",
      "displayFormat": "date:DD/MM/YYYY"
    },
    {
      "canonicalKey": null,
      "fieldKey": "ket_luan_xet_duyet",
      "label": "Kết luận xét duyệt",
      "fieldType": "Select",
      "isReadOnly": false,
      "required": true,
      "options": ["Đạt tiêu chuẩn", "Không đạt — cần bổ sung", "Cần hội chẩn"]
    },
    {
      "canonicalKey": null,
      "fieldKey": "ghi_chu_bac_si",
      "label": "Ghi chú của bác sĩ",
      "fieldType": "Textarea",
      "isReadOnly": false
    }
  ]
}

# → Response:
# {
#   "moduleCode": "datamatch",
#   "screenCode": "patient-review",
#   "formKey": "patient-data",
#   "formTemplateId": "<form-uuid>",
#   "fieldsGenerated": 8
# }
# Screen và Form đã được publish tự động.


### ════════════════════════════════════════════════════════════
### PHẦN 3 — FRONTEND: Đọc layout và render form
### ════════════════════════════════════════════════════════════

### Bước 3.1 — Frontend gọi layout (generic, không cần biết gì về DataMatching)
GET http://localhost:5000/forms/screens/datamatch/patient-review/layout

# Response trả về:
# {
#   "dataSources": [
#     {
#       "namespace": "record",
#       "serviceId": "datamatch",
#       "resourcePath": "/dm/records/{recordId}",
#       "requiredParams": ["recordId"]
#     }
#   ],
#   "tabs": [{
#     "widgets": [{
#       "widgetType": "FormSection",
#       "formSchema": {
#         "fields": [
#           { "key": "mabenhnnhan", "label": "Mã bệnh nhân",
#             "dataBinding": { "expression": "{{sources.record.MaBenhNhan}}", "displayFormat": null },
#             "isReadOnly": true },
#           { "key": "tenbenhnnhan", "label": "Họ tên bệnh nhân",
#             "dataBinding": { "expression": "{{sources.record.TenBenhNhan}}", "displayFormat": null },
#             "isReadOnly": true },
#           ...
#           { "key": "ket_luan_xet_duyet", "label": "Kết luận xét duyệt",
#             "dataBinding": null, "isReadOnly": false },
#           { "key": "ghi_chu_bac_si", "label": "Ghi chú của bác sĩ",
#             "dataBinding": null, "isReadOnly": false }
#         ]
#       }
#     }]
#   }]
# }

### Bước 3.2 — Frontend fetch dữ liệu record (thay <record-id>)
GET http://localhost:5000/dm/records/<record-id>

# Response:
# {
#   "canonicalPayload": {
#     "TenBenhNhan": "Nguyễn Văn An",
#     "NgaySinh": "1985-03-15",
#     "MaBenhNhan": "BN2024001",
#     "KhoaDieuTri": "Khoa Tim Mạch",
#     "ChanDoan": "Tăng huyết áp độ II",
#     "NgayNhapVien": "2026-06-01"
#   }
# }

### Bước 3.3 — Submit form (chỉ gửi field tự do)
POST http://localhost:5000/forms/datamatch/patient-data/submit
Content-Type: application/json
Authorization: Bearer <token>

{
  "answers": [
    { "fieldKey": "ket_luan_xet_duyet", "value": "Đạt tiêu chuẩn" },
    { "fieldKey": "ghi_chu_bac_si",     "value": "Bệnh nhân đủ điều kiện nhập viện. Đã kiểm tra xét nghiệm." }
  ]
}
```

---

## Frontend — Code mẫu (TypeScript/React)

Đây là phần frontend chạy được từ layout response. **Không cần sửa khi admin thêm screen/field mới.**

```typescript
// types.ts
interface DataSource {
  namespace: string;
  serviceId: string;
  resourcePath: string;
  requiredParams: string[];
}

interface DataBinding {
  expression: string;
  displayFormat: string | null;
}

interface FormField {
  id: string;
  key: string;
  label: string;
  type: string;
  isReadOnly: boolean;
  dataBinding: DataBinding | null;
  options?: Array<{ label: string; value: string }>;
  required: boolean;
}
```

```typescript
// useFormScreen.ts — hook generic, dùng lại mọi screen
export function useFormScreen(
  moduleCode: string,
  screenCode: string,
  routeParams: Record<string, string>   // { recordId: "abc-123" }
) {
  const [layout, setLayout]     = useState<ScreenLayout | null>(null);
  const [sourceData, setSourceData] = useState<Record<string, any>>({});
  const [loading, setLoading]   = useState(true);

  useEffect(() => {
    async function load() {
      // 1. Fetch layout từ DynamicFormService
      const layout = await fetch(
        `/forms/screens/${moduleCode}/${screenCode}/layout`
      ).then(r => r.json()).then(r => r.data);

      // 2. Fetch tất cả dataSources song song
      const sources: Record<string, any> = {};
      await Promise.all(
        layout.dataSources.map(async (ds: DataSource) => {
          // Substitute {param} trong resourcePath
          const url = ds.resourcePath.replace(
            /\{(\w+)\}/g,
            (_, key) => routeParams[key] ?? ''
          );
          const resp = await fetch(url).then(r => r.json());
          // DataMatchingService trả về { data: { canonicalPayload: {...} } }
          sources[ds.namespace] = resp.data?.canonicalPayload ?? resp.data ?? resp;
        })
      );

      setLayout(layout);
      setSourceData(sources);
      setLoading(false);
    }
    load();
  }, [moduleCode, screenCode, JSON.stringify(routeParams)]);

  return { layout, sourceData, loading };
}
```

```typescript
// evaluateExpression.ts — evaluate {{sources.namespace.path}}
export function evaluateExpression(
  expression: string,
  sources: Record<string, any>
): string | null {
  const match = expression.match(/^\{\{sources\.(\w+)\.(.+)\}\}$/);
  if (!match) return null;

  const [, namespace, path] = match;
  const root = sources[namespace];
  if (!root) return null;

  // Traverse dot-notation path
  return path.split('.').reduce(
    (obj: any, key) => (obj != null ? obj[key] : null),
    root
  ) ?? null;
}

export function applyFormat(value: string | null, format: string | null): string {
  if (!value || !format) return value ?? '';
  const [type, pattern] = format.split(':');
  if (type === 'date') {
    // Đơn giản: parse ISO date và format theo pattern DD/MM/YYYY
    const d = new Date(value);
    if (isNaN(d.getTime())) return value;
    return pattern
      .replace('DD',   String(d.getDate()).padStart(2, '0'))
      .replace('MM',   String(d.getMonth() + 1).padStart(2, '0'))
      .replace('YYYY', String(d.getFullYear()));
  }
  return value;
}
```

```typescript
// DynamicFormScreen.tsx — component generic render mọi screen
export function DynamicFormScreen({
  moduleCode, screenCode, routeParams
}: {
  moduleCode: string;
  screenCode: string;
  routeParams: Record<string, string>;
}) {
  const { layout, sourceData, loading } = useFormScreen(moduleCode, screenCode, routeParams);
  const [formValues, setFormValues] = useState<Record<string, string>>({});

  if (loading) return <div>Đang tải...</div>;

  // Lấy formSchema từ widget đầu tiên kiểu FormSection
  const formWidget = layout?.tabs[0]?.widgets.find(w => w.widgetType === 'FormSection');
  const fields: FormField[] = formWidget?.formSchema?.fields ?? [];

  function getFieldValue(field: FormField): string {
    if (!field.dataBinding) return formValues[field.key] ?? '';
    const raw = evaluateExpression(field.dataBinding.expression, sourceData);
    return applyFormat(raw, field.dataBinding.displayFormat);
  }

  async function handleSubmit() {
    const answers = fields
      .filter(f => !f.isReadOnly)
      .map(f => ({ fieldKey: f.key, value: formValues[f.key] ?? null }));

    await fetch(`/forms/${moduleCode}/${layout!.formKey}/submit`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ answers }),
    });
  }

  return (
    <form onSubmit={e => { e.preventDefault(); handleSubmit(); }}>
      {fields.map(field => (
        <div key={field.key} style={{ marginBottom: 16 }}>
          <label>{field.label}{field.required && ' *'}</label>
          {field.isReadOnly ? (
            <div className="readonly-value">{getFieldValue(field)}</div>
          ) : field.type === 'select' ? (
            <select
              required={field.required}
              value={formValues[field.key] ?? ''}
              onChange={e => setFormValues(v => ({ ...v, [field.key]: e.target.value }))}
            >
              <option value="">-- Chọn --</option>
              {field.options?.map(o => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>
          ) : (
            <textarea
              required={field.required}
              value={formValues[field.key] ?? ''}
              onChange={e => setFormValues(v => ({ ...v, [field.key]: e.target.value }))}
            />
          )}
        </div>
      ))}
      <button type="submit">Gửi xét duyệt</button>
    </form>
  );
}

// Sử dụng:
// URL: /review/patients/:recordId
// <DynamicFormScreen
//   moduleCode="datamatch"
//   screenCode="patient-review"
//   routeParams={{ recordId: params.recordId }}
// />
```

---

## Kết quả giao diện

```
┌─────────────────────────────────────────────────────────┐
│  Xét duyệt hồ sơ bệnh nhân                             │
├─────────────────────────────────────────────────────────┤
│  Mã bệnh nhân          [BN2024001            ] 🔒       │
│  Họ tên bệnh nhân      [Nguyễn Văn An        ] 🔒       │
│  Ngày sinh             [15/03/1985           ] 🔒       │
│  Khoa điều trị         [Khoa Tim Mạch        ] 🔒       │
│  Chẩn đoán             [Tăng huyết áp độ II  ] 🔒       │
│  Ngày nhập viện        [01/06/2026           ] 🔒       │
│                                                         │
│  Kết luận xét duyệt *                                   │
│  [-- Chọn --                              ▼]            │
│                                                         │
│  Ghi chú của bác sĩ                                     │
│  [                                          ]           │
│  [                                          ]           │
│                                                         │
│                      [Gửi xét duyệt]                   │
└─────────────────────────────────────────────────────────┘

🔒 = readOnly, pre-filled từ DataMatchingService
    = user điền
```

---

## Khi muốn thêm loại bệnh nhân mới

Không cần sửa frontend. Chỉ cần:

```
1. Đăng ký SourceProfile mới:
   POST /dm/sources
   { sourceSystem: "bhyt-hn", recordType: "chung-tu", mappings: {...} }

2. Auto-generate form mới:
   POST /forms/admin/generate-from-source
   { moduleCode: "datamatch", screenCode: "chung-tu-review", ... }

3. Frontend dùng ngay:
   <DynamicFormScreen moduleCode="datamatch" screenCode="chung-tu-review" ... />
```

---

## Lakehouse view as source (Phase 2)

Từ doc 44 — Unified Ingest Pipeline, dữ liệu từ lakehouse PostgreSQL view cũng chảy vào `/dm/records/{id}` thay vì endpoint riêng. Flow giống hệt HIS/BHYT, chỉ khác bước **publish event** (LakehouseService poll view → publish `RawRecordIngestRequestedIntegrationEvent` → DataMatching consume).

**Quy trình đăng ký 1 source lakehouse:**

```
[1] DE: cấp VIEW + GRANT SELECT hdos_reader   (xem doc 43)

[2] BE/Admin: đăng ký SourceProfile (mapping field DB → canonical)
    POST /dm/sources
    {
      "sourceSystem":     "lakehouse:v_lab_results_v1",
      "recordType":       "lab-result",
      "businessKeyField": "MaBenhNhan",
      "mappings": {
        "business_key": "MaBenhNhan",
        "hba1c":        "HbA1c",
        "blood_glucose":"Glucose",
        ...
      }
    }

[3] BE/Admin: đăng ký ViewBinding (view → SourceProfile)
    POST /lakehouse/view-bindings
    {
      "viewName":           "warehouse.v_lab_results_v1",
      "sourceSystem":       "lakehouse:v_lab_results_v1",
      "recordType":         "lab-result",
      "businessKeyColumn":  "business_key",
      "updatedAtColumn":    "updated_at",
      "pollIntervalSeconds":300
    }

[4] WarehousePollerWorker tự pick up → record xuất hiện ở /dm/records/...

[5] Auto-generate form (giống hệt bước 2.1 ở "PHẦN 2" phía trên):
    POST /forms/admin/generate-from-source
    {
      "moduleCode": "lab",
      "screenCode": "lab-result-detail",
      "dataSource": {
        "namespace":      "record",
        "resourcePath":   "/dm/records/{recordId}",
        "requiredParams": ["recordId"]
      },
      "fields": [...]
    }

[6] Frontend dùng ngay — KHÔNG cần code FE mới:
    <DynamicFormScreen moduleCode="lab" screenCode="lab-result-detail"
                       routeParams={{ recordId }} />
```

**Điểm quan trọng:** mọi source — HIS REST push, BHYT file, lakehouse view, API ngoài — đều hiển thị qua **cùng một `<DynamicFormScreen>` component** với cùng DataSource `/dm/records/{id}`. FE không có if-branch theo source. Xem doc 44 mục 5 (Phân chia trách nhiệm) và mục 7 (Cách thêm source mới).

---

## Checklist để chạy

```
[ ] docker compose up -d — đảm bảo DataMatchingService + DynamicFormService đang chạy
[ ] Chạy migration DynamicFormService (nếu chưa): dotnet ef database update ...
[ ] Chạy HTTP script trên (Phần 1 → Phần 2 → Phần 3)
[ ] Đợi ~30s sau Phần 1 để MatchingWorker xử lý record
[ ] Kiểm tra GET /dm/records?sourceSystem=his-01 → status = "Matched"
[ ] Gọi POST /forms/admin/generate-from-source → lấy screenCode + formKey
[ ] Gọi GET /forms/screens/datamatch/patient-review/layout → xem dataSources + expressions
```
