# 63 — FE Guide: 4 API của Lakehouse Data Contracts

> **Mục đích:** Hướng dẫn FE consume nhóm API `Lakehouse Data Contracts — Universal funnel` (doc 53). Toàn bộ TypeScript types khớp 1-1 với BE record, code snippet chạy được.
>
> **Companion:** doc 53 (engine), doc 58/60 (FE integration cũ), doc 62 (worked example pharmacy contract).

---

## 1. Tổng quan 4 endpoint

Toàn bộ 4 endpoint nằm trong **1 controller** (`DataContractChartController`) và **chia sẻ chung**:

- **Bearer JWT** (token từ AuthService)
- **Envelope** `ApiResponse<T>` — wrap mọi response success / fail
- **Dispatch** qua `DataContractGateway` bằng reflection trên `SchemaType` → thêm contract mới ở BE không cần đụng controller
- **Discovery first** — FE nên gọi `/contracts` trước để biết có contract nào

```
                     ┌──────────────────────────────────┐
                     │  GET /lakehouse/contracts        │  ① Discovery
                     │  → list contract code            │
                     └──────────────────────────────────┘
                                     │
                                     ▼
        ┌──────────────────────────────────────────────────────────┐
        │  Cho 1 contract code, FE có 3 endpoint phụ:              │
        ├──────────────────────────────────────────────────────────┤
        │  GET /lakehouse/contracts/{code}/schema  ② Metadata field│
        │  GET /lakehouse/contracts/{code}/chart   ③ Dashboard     │
        │  GET /lakehouse/contracts/{code}/prefill ④ Form data     │
        └──────────────────────────────────────────────────────────┘
```

### Bảng so sánh

| # | Endpoint | Output type | Khi nào FE gọi | Caller chính |
|---|----------|-------------|----------------|--------------|
| ① | `GET /contracts` | `DataContractMetadata[]` | App init / Admin chọn contract trong dropdown | Admin UI catalog |
| ② | `GET /contracts/{code}/schema` | `FieldDescriptor[]` | Admin build form / map field → expression | Field Browser, Form Designer |
| ③ | `GET /contracts/{code}/chart` | `SduiPage` | Render dashboard với nhiều chart | Chart widget, Dashboard page |
| ④ | `GET /contracts/{code}/prefill` | `FormPrefillResult` | Bind data vào field của form | DynamicForm screen renderer |

---

## 2. Common: Envelope `ApiResponse<T>`

Tất cả 4 endpoint trả về cùng shape:

```ts
interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  errorCode: string | null;
  errorMessage: string | null;
}
```

**Quy ước FE:**

```ts
async function callApi<T>(url: string, token: string): Promise<T> {
  const res = await fetch(url, {
    headers: { Authorization: `Bearer ${token}` },
  });
  const env: ApiResponse<T> = await res.json();

  if (!env.success || env.data === null) {
    throw new ApiError(env.errorCode ?? "UNKNOWN", env.errorMessage ?? "Unknown error");
  }
  return env.data;
}

class ApiError extends Error {
  constructor(public code: string, public msg: string) { super(`[${code}] ${msg}`); }
}
```

**Header bắt buộc** cho mọi call:
```
Authorization: Bearer <JWT từ AuthService>
```

---

## 3. ① `GET /lakehouse/contracts` — Discovery

> Liệt kê tất cả contract đã đăng ký ở BE. Không có path/query param. Idempotent, nhanh, FE nên cache.

### Request

```http
GET /lakehouse/contracts HTTP/1.1
Authorization: Bearer <token>
```

### Response

```ts
interface DataContractMetadata {
  code: string;            // "pharmacy.dispense.daily.row" — unique identifier
  displayName: string;     // "Phát thuốc theo ngày × khoa × nhóm thuốc"
  schemaTypeName: string;  // "PharmacyDispenseDailyRow" — C# class name, FE thường không cần
}

// Response body
type ListContractsResponse = ApiResponse<DataContractMetadata[]>;
```

### Ví dụ response

```json
{
  "success": true,
  "data": [
    { "code": "finance.daily.row",            "displayName": "Tài chính theo ngày × khoa (row-level)", "schemaTypeName": "FinanceDailyRow" },
    { "code": "finance.monthly.row",          "displayName": "Tài chính tháng × khoa",                  "schemaTypeName": "FinanceMonthlyRow" },
    { "code": "patient.daily.new",            "displayName": "Bệnh nhân đăng ký mới theo ngày × khoa", "schemaTypeName": "PatientDailyNewRow" },
    { "code": "pharmacy.dispense.daily.row",  "displayName": "Phát thuốc theo ngày × khoa × nhóm thuốc","schemaTypeName": "PharmacyDispenseDailyRow" }
  ],
  "errorCode": null,
  "errorMessage": null
}
```

### Use case FE

- **Catalog UI** — admin chọn contract trong dropdown khi gắn DataSource vào screen
- **Sanity check** — startup app fetch list, warn nếu contract đang ref không còn trong list
- **Auto-suggest** — gợi ý contract khi user gõ search

### Recommend

- Cache **trong app session** (TTL ∼30 phút). List ít đổi.
- Sort theo `code` ở FE (BE đã sort sẵn nhưng đừng phụ thuộc).

---

## 4. ② `GET /lakehouse/contracts/{code}/schema` — Field metadata

> Reflection trên `SchemaType` của contract → trả danh sách field cùng type. Dùng cho Field Browser, Form Designer auto-suggest expression.

### Request

```http
GET /lakehouse/contracts/pharmacy.dispense.daily.row/schema HTTP/1.1
Authorization: Bearer <token>
```

| Path param | Type | Mô tả |
|----|----|----|
| `code` | string | Contract code, lấy từ endpoint ① |

### Response

```ts
interface FieldDescriptor {
  name: string;       // "DepartmentName" — PascalCase (C# property)
  jsonName: string;   // "departmentName" — camelCase (giống key trong /chart và /prefill)
  type: string;       // "String", "Int32", "DateOnly", "Decimal", ...
  optional: boolean;  // true nếu nullable (nullable value type hoặc nullable reference type)
}

type SchemaResponse = ApiResponse<FieldDescriptor[]>;
```

### Ví dụ response — `pharmacy.dispense.daily.row`

```json
{
  "success": true,
  "data": [
    { "name": "DispenseDate",       "jsonName": "dispenseDate",       "type": "DateOnly", "optional": false },
    { "name": "DepartmentId",       "jsonName": "departmentId",       "type": "Int32",    "optional": false },
    { "name": "DepartmentName",     "jsonName": "departmentName",     "type": "String",   "optional": false },
    { "name": "DrugGroup",          "jsonName": "drugGroup",          "type": "String",   "optional": false },
    { "name": "PrescriptionCount",  "jsonName": "prescriptionCount",  "type": "Int32",    "optional": false },
    { "name": "DoseCount",          "jsonName": "doseCount",          "type": "Int32",    "optional": false },
    { "name": "TotalAmount",        "jsonName": "totalAmount",        "type": "Decimal",  "optional": false },
    { "name": "PatientServedCount", "jsonName": "patientServedCount", "type": "Int32",    "optional": false },
    { "name": "StockAlertLevel",    "jsonName": "stockAlertLevel",    "type": "String",   "optional": true  }
  ]
}
```

### Use case FE

- **Field Browser** trong Form Designer: hiển thị danh sách field để admin pick → tự sinh expression `{{sources.<ns>.<jsonName>}}`
- **Type-aware widget**: chọn widget mặc định theo `type` (DateOnly → DatePicker, Decimal → NumberInput, String → TextField)
- **Validation hint**: `optional=false` field → đánh dấu required ở form

### Mapping type C# → widget gợi ý

| C# type | Suggested widget | Display format default |
|---|---|---|
| `DateOnly`, `DateTime` | DatePicker (read-only) | `date:DD/MM/YYYY` |
| `Int32`, `Int64` | NumberInput | `number` |
| `Decimal`, `Double` | NumberInput (decimal) | `currency:VND` (nếu field có "Amount") |
| `String` | TextField | none |
| `Boolean` | Switch / Checkbox | none |

### Recommend

- Cache theo **`code`** (per-contract). Schema rất ít đổi.
- Khi update schema BE → bump version contract (xem doc 53 §6.3) → FE clear cache theo code mới.

---

## 5. ③ `GET /lakehouse/contracts/{code}/chart` — SduiPage dashboard

> Stream rows → Consumer aggregate + build chart → trả `SduiPage` đã sẵn shape cho SDUI renderer.

### Request

```http
GET /lakehouse/contracts/pharmacy.dispense.daily.row/chart?source=demo&date=2026-06-11
Authorization: Bearer <token>
```

| Param | Vị trí | Required | Mô tả |
|----|----|----|----|
| `code` | path | ✅ | Contract code |
| `source` | query | optional | Source code (`sql`, `demo`,...). Default = source đầu tiên đăng ký |
| `consumer` | query | optional | Consumer code. Default = `"chart"` |
| **mọi query khác** | query | tuỳ contract | Pass nguyên vào `DataContractQuery.Filters` (date, department, group,...) |

### Response

```ts
interface SduiPage {
  code: string;        // "pharmacy-dispense-daily"
  title: string;
  badge: string | null;
  live: boolean;
  subtitle: string | null;
  actions: SduiAction[];
  rows: SduiRow[];
  generatedAt: string; // ISO 8601 UTC
}

interface SduiAction {
  label: string;
  variant: string;
  color: string | null;
}

interface SduiRow {
  components: SduiComponent[];
}

// SduiComponent là discriminated union — `type` quyết shape props
type SduiComponent =
  | { type: "KpiCard";      span: number; props: KpiCardProps }
  | { type: "ProgressList"; span: number; props: ProgressListProps }
  | { type: "AlertList";    span: number; props: AlertListProps }
  | { type: "FlowPipeline"; span: number; props: FlowPipelineProps }
  | { type: "ChartPie";     span: number; props: ChartPieProps };

interface KpiCardProps {
  title: string;
  value: unknown;            // có thể là số, chuỗi formatted, ...
  accent: string | null;     // hex color cho border/icon
  hint: string | null;       // sub-text
  hintColor: string | null;
}

interface ProgressItem {
  label: string;
  value: number;             // 0-100
  secondaryValue: number | null;
  color: string | null;      // hex
}

interface FooterAction {
  label: string;
  variant: string;
}

interface ProgressListProps {
  title: string;
  headerAction: string | null;
  maxValue: number;          // thường = 100
  items: ProgressItem[];
  footerActions: FooterAction[] | null;
}

interface AlertItem {
  code: string;              // "K#3"
  text: string;
  patient: string;
  dept: string;
  time: string;              // human-readable, không phải ISO
  severity: "critical" | "warning" | "info";
}

interface AlertListProps {
  title: string;
  realtimeBadge: boolean;    // show "Live" indicator
  maxHeight: number | null;  // px, scroll nếu list dài
  totalCount: number;
  items: AlertItem[];
}

interface FlowStage {
  label: string;
  value: number;
  color: string | null;
}

interface FlowPipelineProps {
  title: string;
  footer: string | null;
  stages: FlowStage[];       // 2-5 stage thường
}

interface ChartPieData {
  label: string;
  value: number;
}

interface ChartPieProps {
  title: string;
  height: number | null;
  variant: "pie" | "donut" | null;
  legend: boolean;
  data: ChartPieData[];
  colors: string[] | null;   // hex palette
}

type ChartResponse = ApiResponse<SduiPage>;
```

### Ví dụ response (rút gọn)

```json
{
  "success": true,
  "data": {
    "code": "pharmacy-dispense-daily",
    "title": "Phát thuốc theo ngày (DataContract)",
    "badge": "Contract",
    "live": true,
    "subtitle": "Qua DataContractGateway · 14:23 · Ngày 11/06/2026",
    "actions": [{ "label": "Xuất Excel", "variant": "default", "color": null }],
    "rows": [
      {
        "components": [
          { "type": "KpiCard", "span": 6, "props": { "title": "Tổng đơn thuốc", "value": 328, "accent": "#1677ff", "hint": "1086 lượt phục vụ", "hintColor": null } },
          { "type": "KpiCard", "span": 6, "props": { "title": "Tổng liều phát",  "value": 4892, "accent": "#13c2c2", "hint": "AVG: 14.9 liều/đơn", "hintColor": null } },
          { "type": "KpiCard", "span": 6, "props": { "title": "Tổng giá trị",   "value": "675 tr", "accent": "#722ed1", "hint": "VNĐ", "hintColor": null } },
          { "type": "KpiCard", "span": 6, "props": { "title": "% Kháng sinh",   "value": "36.4%", "accent": "#faad14", "hint": "1780 liều KS", "hintColor": null } }
        ]
      },
      {
        "components": [
          { "type": "ProgressList", "span": 16, "props": { "title": "Top 15 khoa theo số liều (màu = % kháng sinh)", "maxValue": 100, "items": [...] } },
          { "type": "AlertList",    "span":  8, "props": { "title": "Cảnh báo kho + lạm dụng kháng sinh", "realtimeBadge": true, "totalCount": 4, "items": [...] } }
        ]
      },
      {
        "components": [
          { "type": "FlowPipeline", "span": 12, "props": { "title": "Dòng phát thuốc", "stages": [...] } },
          { "type": "ChartPie",     "span": 12, "props": { "title": "Phân bổ liều theo nhóm thuốc", "variant": "donut", "data": [...] } }
        ]
      }
    ],
    "generatedAt": "2026-06-11T07:23:11.456Z"
  }
}
```

### Use case FE

- Dashboard page chart-only
- ChartEmbed widget bên trong FormScreen (xem doc 52)
- Drill-down: click 1 dept trên ProgressList → mở screen chi tiết

### FE rendering pattern

```ts
import dayjs from "dayjs";

function renderSduiPage(page: SduiPage, container: HTMLElement) {
  container.innerHTML = "";
  for (const row of page.rows) {
    const rowEl = document.createElement("div");
    rowEl.className = "sdui-row grid grid-cols-24 gap-4";
    for (const comp of row.components) {
      const compEl = document.createElement("div");
      compEl.style.gridColumn = `span ${comp.span ?? 24}`;
      compEl.appendChild(renderComponent(comp));
      rowEl.appendChild(compEl);
    }
    container.appendChild(rowEl);
  }
}

function renderComponent(c: SduiComponent): HTMLElement {
  switch (c.type) {
    case "KpiCard":      return renderKpiCard(c.props);
    case "ProgressList": return renderProgressList(c.props);
    case "AlertList":    return renderAlertList(c.props);
    case "FlowPipeline": return renderFlowPipeline(c.props);
    case "ChartPie":     return renderChartPie(c.props);
  }
}
```

### Recommend

- **KHÔNG cache** `/chart` — data có thể realtime hoặc đổi theo filter
- Loading state mỗi lần thay filter `?date=...` / `?department=...`
- Empty state: nếu `rows.length === 0` → BE đã trả `BuildEmpty(...)` với `subtitle` chứa hint
- Grid 24-col chuẩn — tổng `span` mỗi row = 24

---

## 6. ④ `GET /lakehouse/contracts/{code}/prefill` — Flat dict cho form

> Stream rows → Consumer chỉ map field thành dict phẳng → FE bind vào field của DynamicForm.

### Request

```http
GET /lakehouse/contracts/pharmacy.dispense.daily.row/prefill?source=demo&mode=single&department=2
Authorization: Bearer <token>
```

| Param | Vị trí | Required | Mô tả |
|----|----|----|----|
| `code` | path | ✅ | Contract code |
| `source` | query | optional | Source code. Default = source đầu tiên đăng ký |
| `mode` | query | optional | `single` → trả thêm `single` object (1 row phẳng) |
| `limit` | query | optional | Cap số row. Default = 50 |
| **mọi query khác** | query | tuỳ contract | Pass nguyên vào filter |

### Response

```ts
interface FormPrefillResult {
  contractCode: string;
  rowCount: number;
  rows: Record<string, unknown>[];      // luôn có
  single: Record<string, unknown> | null; // chỉ có khi ?mode=single
}

type PrefillResponse = ApiResponse<FormPrefillResult>;
```

Key trong `rows[i]` và `single` là **camelCase**, khớp với `jsonName` từ endpoint ② `/schema`.

### Ví dụ response — `?mode=single`

```json
{
  "success": true,
  "data": {
    "contractCode": "pharmacy.dispense.daily.row",
    "rowCount": 4,
    "rows": [
      { "dispenseDate": "2026-06-11", "departmentId": 2, "departmentName": "Khoa Cấp cứu", "drugGroup": "Kháng sinh", "prescriptionCount": 88, "doseCount": 612, "totalAmount": 72300000, "patientServedCount": 124, "stockAlertLevel": "low" },
      { "dispenseDate": "2026-06-11", "departmentId": 2, "departmentName": "Khoa Cấp cứu", "drugGroup": "Giảm đau",   "prescriptionCount": 72, "doseCount": 488, "totalAmount": 21800000, "patientServedCount": 118, "stockAlertLevel": "low" },
      ...
    ],
    "single": {
      "dispenseDate": "2026-06-11",
      "departmentId": 2,
      "departmentName": "Khoa Cấp cứu",
      "drugGroup": "Kháng sinh",
      "prescriptionCount": 88,
      "doseCount": 612,
      "totalAmount": 72300000,
      "patientServedCount": 124,
      "stockAlertLevel": "low"
    }
  }
}
```

### Use case FE

- DynamicForm screen — fill field từ data source
- Table widget — render rows
- Auto-fill default value trong form input

### Kind=Single vs Kind=List (doc 60)

| `kind` ở DataSource (lấy từ layout) | URL FE append | Dùng |
|---|---|---|
| `Single` | `?mode=single` | Đọc `data.single` — 1 record bind vào nhiều field |
| `List` | (không) | Đọc `data.rows` — render table |

### Expression resolution

Trong DynamicForm screen, field có `dataBinding.expression` dạng:

```
{{sources.<namespace>.<jsonName>}}
```

- `<namespace>` = `DataSource.Namespace` admin đã đặt (không phải `code`)
- `<jsonName>` = key trong `single` (camelCase, khớp `/schema`)

```ts
// Sau khi fetch /prefill?mode=single
const sources: Record<string, Record<string, unknown>> = {
  pharmacy: prefillResult.single ?? {},
};

// Expression resolver (xem doc 60 §6)
function resolveExpression(expr: string, sources: SourceMap): unknown {
  const m = expr.match(/^\{\{sources\.([\w-]+)\.([\w.-]+)\}\}$/);
  if (!m) return expr; // hoặc interpolation
  return sources[m[1]]?.[m[2]];
}

resolveExpression("{{sources.pharmacy.doseCount}}", sources);
// → 612
```

### Recommend

- **KHÔNG cache** `/prefill` — data thường thay đổi
- Loading state mỗi field khi fetch
- Nếu `single === null` → field hiển thị placeholder "Không có dữ liệu"
- Default `limit=50` đủ cho hầu hết case; nâng nếu table cần nhiều row hơn

---

## 7. Integration patterns

### Pattern A — Catalog UI (Admin chọn contract)

```ts
async function loadCatalog(token: string) {
  const contracts = await callApi<DataContractMetadata[]>("/lakehouse/contracts", token);
  return contracts.sort((a, b) => a.code.localeCompare(b.code));
}

// Khi admin chọn 1 contract → load schema để hiển thị field list
async function loadFields(code: string, token: string) {
  return await callApi<FieldDescriptor[]>(`/lakehouse/contracts/${code}/schema`, token);
}
```

### Pattern B — Dashboard chart-only

```ts
async function renderDashboard(code: string, filters: Record<string, string>, token: string) {
  const qs = new URLSearchParams(filters).toString();
  const url = `/lakehouse/contracts/${code}/chart${qs ? `?${qs}` : ""}`;
  const page = await callApi<SduiPage>(url, token);
  renderSduiPage(page, document.getElementById("dashboard")!);
}

renderDashboard("pharmacy.dispense.daily.row",
  { source: "demo", date: "2026-06-11" }, token);
```

### Pattern C — DynamicForm screen (full flow)

```ts
async function renderScreen(moduleCode: string, screenCode: string, urlParams: URLSearchParams, token: string) {
  // 1. Layout
  const layout = await callApi<ScreenLayoutDto>(
    `/forms/screens/${moduleCode}/${screenCode}/layout`, token);

  // 2. Resolve DataSources — fetch parallel
  const sources: SourceMap = {};
  await Promise.all(layout.dataSources.map(async (ds) => {
    const params = { ...ds.defaultParams, ...Object.fromEntries(urlParams) };
    const path = substitute(ds.resourcePath!, params);
    let url = `${ds.baseUrl}${path}`;
    const qs = new URLSearchParams();
    if (ds.kind === "Single") qs.append("mode", "single");
    // các param thừa (không trong placeholder) → query string
    for (const [k, v] of Object.entries(params)) {
      if (!ds.resourcePath!.includes(`{${k}}`)) qs.append(k, String(v));
    }
    if (qs.toString()) url += `?${qs}`;

    const prefill = await callApi<FormPrefillResult>(url, token);
    sources[ds.namespace] = ds.kind === "Single" ? (prefill.single ?? {}) : prefill;
  }));

  // 3. Resolve mỗi field expression
  for (const tab of layout.tabs) {
    for (const widget of tab.widgets) {
      if (widget.formSchema) {
        for (const field of widget.formSchema.fields) {
          if (field.dataBinding) {
            field.value = resolveExpression(field.dataBinding.expression, sources);
            field.displayValue = formatValue(field.value, field.dataBinding.displayFormat);
          }
        }
      }
    }
  }

  renderLayout(layout);
}
```

---

## 8. Worked example end-to-end — Pharmacy contract

### Bước 1: Discovery

```bash
curl -k -H "Authorization: Bearer $TOKEN" \
  https://localhost:8443/lakehouse/contracts | jq '.data[] | select(.code | contains("pharmacy"))'
```
```json
{ "code": "pharmacy.dispense.daily.row", "displayName": "Phát thuốc theo ngày × khoa × nhóm thuốc", "schemaTypeName": "PharmacyDispenseDailyRow" }
```

### Bước 2: Schema → biết field nào tồn tại

```bash
curl -k -H "Authorization: Bearer $TOKEN" \
  https://localhost:8443/lakehouse/contracts/pharmacy.dispense.daily.row/schema | jq '.data[] | .jsonName'
```
```
"dispenseDate" "departmentId" "departmentName" "drugGroup"
"prescriptionCount" "doseCount" "totalAmount" "patientServedCount" "stockAlertLevel"
```

### Bước 3a: Render Dashboard

```bash
curl -k -H "Authorization: Bearer $TOKEN" \
  "https://localhost:8443/lakehouse/contracts/pharmacy.dispense.daily.row/chart?source=demo&date=2026-06-11" \
  | jq '.data | { title, rowCount: (.rows | length), componentTypes: [.rows[].components[].type] }'
```
```json
{
  "title": "Phát thuốc theo ngày (DataContract)",
  "rowCount": 3,
  "componentTypes": ["KpiCard","KpiCard","KpiCard","KpiCard","ProgressList","AlertList","FlowPipeline","ChartPie"]
}
```

### Bước 3b: Prefill 1 record cho form

```bash
curl -k -H "Authorization: Bearer $TOKEN" \
  "https://localhost:8443/lakehouse/contracts/pharmacy.dispense.daily.row/prefill?source=demo&mode=single&department=2" \
  | jq '.data.single'
```
```json
{
  "dispenseDate": "2026-06-11",
  "departmentId": 2,
  "departmentName": "Khoa Cấp cứu",
  "drugGroup": "Kháng sinh",
  "prescriptionCount": 88,
  "doseCount": 612,
  "totalAmount": 72300000,
  "patientServedCount": 124,
  "stockAlertLevel": "low"
}
```

### Bước 4: Gắn field expression trong FormDesigner

| Field | Expression | DisplayFormat | Resolve thành |
|----|----|----|----|
| Khoa | `{{sources.pharmacy.departmentName}}` | none | "Khoa Cấp cứu" |
| Tổng giá trị | `{{sources.pharmacy.totalAmount}}` | `currency:VND` | "72.300.000 ₫" |
| Ngày phát | `{{sources.pharmacy.dispenseDate}}` | `date:DD/MM/YYYY` | "11/06/2026" |
| Cảnh báo kho | `{{sources.pharmacy.stockAlertLevel}}` | none | "low" |

---

## 9. Error handling — 5 case cần xử lý

| HTTP | `errorCode` | Triệu chứng | FE làm gì |
|---|---|---|---|
| 404 | `CONTRACT.NOT_FOUND` | Contract code sai hoặc chưa register | Toast "Contract không tồn tại". Re-fetch `/contracts` để refresh list |
| 404 | `CONSUMER.NOT_FOUND` | Contract có nhưng chưa register consumer cho output type này | Hiếm — chỉ xảy ra với contract dev đang viết. Báo BE |
| 404 | `SOURCE.NOT_FOUND` | URL truyền `?source=xxx` không match SourceCode nào | Bỏ `?source=xxx` để dùng source default; hoặc check log BE |
| 401 / 403 | (no envelope) | Token hết hạn / không đủ permission | Refresh token, hoặc redirect login |
| 5xx | — | BE crash | Console error + fallback empty state |

```ts
async function safeFetch<T>(url: string, token: string, fallback: T): Promise<T> {
  try {
    return await callApi<T>(url, token);
  } catch (e) {
    if (e instanceof ApiError && e.code === "CONTRACT.NOT_FOUND") {
      // contract bị xóa khỏi registry → refresh catalog
      invalidateContractCatalog();
    }
    console.warn("DataContract API failed:", e);
    return fallback;
  }
}
```

---

## 10. Quick reference

| Endpoint | Method | Output (data) | Caching | Critical query |
|---|---|---|---|---|
| `/lakehouse/contracts` | GET | `DataContractMetadata[]` | ✅ Session-level | none |
| `/lakehouse/contracts/{code}/schema` | GET | `FieldDescriptor[]` | ✅ Per-code | none |
| `/lakehouse/contracts/{code}/chart` | GET | `SduiPage` | ❌ Never | `source`, filter (date, dept,...) |
| `/lakehouse/contracts/{code}/prefill` | GET | `FormPrefillResult` | ❌ Never | `source`, `mode=single`, `limit`, filter |

**Header bắt buộc:** `Authorization: Bearer <JWT>`
**Envelope:** mọi response wrap `ApiResponse<T>`
**Casing key:** camelCase ở mọi nơi (`departmentName`, không phải `DepartmentName`)
**Discovery first:** gọi `/contracts` → cache → khi cần detail field thì `/schema` → render dùng `/chart` hoặc `/prefill`

---

## 11. Related docs

- [53 — Data Contract Engine architecture](./53-chart-funnel-architecture.md)
- [58 — Lakehouse ↔ DynamicForm Integration](./58-lakehouse-dynamicform-integration.md)
- [60 — FE Integration DataContract Prefill (chi tiết DynamicForm flow)](./60-fe-integration-datacontract-prefill.md)
- [61 — DataSource `defaultParams`](./61-datasource-default-params.md)
- [62 — Pharmacy contract worked example BE](./62-pharmacy-dispense-daily-demo.md)
