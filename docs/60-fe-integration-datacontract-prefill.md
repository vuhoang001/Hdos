# 60 — FE Integration: DataContract Prefill + Chart (qua DynamicForm Screen)

> Recipe cho FE để render screen có DataSource trỏ vào **Lakehouse DataContract** (hoặc bất kỳ Provider auto-sync nào theo doc 59). Worked example: `finance.monthly.row`.
>
> Companion: doc 41 (Loose Coupling), doc 52 (Embed chart), doc 58 (Catalog), doc 59 (Phase 4 auto-sync).

---

## 1. Mental model (3 calls)

```
┌──────────────┐   1. GET /forms/screens/{m}/{c}/layout
│              │ ────────────────────────────────────►  DynamicForm
│   FE Renderer│ ◄────────────────────────────────────  ScreenLayoutDto
│              │                                        (baseUrl, resourcePath, kind, ...)
│              │   2. GET {baseUrl}{resourcePath sub}?mode=single&<params>
│              │ ────────────────────────────────────►  Lakehouse (qua nginx)
│              │ ◄────────────────────────────────────  { single: {...}, rows: [...], ... }
│              │
│              │   3. Resolve {{sources.<ns>.<field>}} cho từng FormField
└──────────────┘
```

FE **không cần biết** Provider/Operation catalog tồn tại. Chỉ dùng các field trong `ScreenLayoutDto.dataSources[*]` để tự fetch.

---

## 2. TypeScript types (match BE)

```ts
// GET /forms/screens/{moduleCode}/{screenCode}/layout
interface ScreenLayoutDto {
  id: string;
  moduleCode: string;
  code: string;
  title: string;
  description: string | null;
  dataSources: DataSourceDto[];
  tabs: ScreenLayoutTabDto[];
  generatedAt: string;
}

interface DataSourceDto {
  namespace: string;                  // "finance" — key trong expression {{sources.finance.X}}
  serviceId: string | null;           // "lakehouse"
  resourcePath: string | null;        // "/lakehouse/contracts/{contractCode}/prefill"
  requiredParams: string[];           // ["contractCode"]
  schemaPath: string | null;          // "/lakehouse/contracts/{contractCode}/schema" hoặc null
  baseUrl: string | null;             // "http://lakehouseservice:8080" — null nếu provider/op inactive
  kind: "Single" | "List" | null;     // "Single" → tự append ?mode=single
  operationId: string | null;         // "lakehouse::prefill"
}

interface FormFieldDto {
  key: string;
  label: string;
  fieldType: string;
  // ... (xem doc 33/34)
  dataBinding: DataBindingDto | null;
}

interface DataBindingDto {
  expression: string;                 // "{{sources.finance.totalRevenue}}"
  displayFormat: string | null;       // "currency:VND" | "date:DD/MM/YYYY" | null
}
```

---

## 3. Resolve DataSource → URL fetch

`resourcePath` chứa **placeholder** dạng `{paramName}`. `requiredParams` list các tên placeholder. FE **phải cung cấp giá trị**, vì BE không lưu — giá trị đến từ:

| Nguồn | Khi nào |
|-------|---------|
| URL query string của route | `?contractCode=finance.monthly.row&year=2026&month=6` |
| App state / Redux | Đã chọn ở màn hình trước |
| Hardcode per-screen | FE biết screen này luôn là contract X (không khuyến khích) |

```ts
function buildDataSourceUrl(
  ds: DataSourceDto,
  params: Record<string, string | number>
): string {
  if (!ds.baseUrl || !ds.resourcePath) {
    throw new Error(`DataSource '${ds.namespace}' inactive — baseUrl/resourcePath null`);
  }

  // 1. Substitute placeholder `{name}` trong path
  let path = ds.resourcePath;
  const missing: string[] = [];
  for (const name of ds.requiredParams) {
    const value = params[name];
    if (value === undefined || value === null || value === "") {
      missing.push(name);
      continue;
    }
    path = path.replaceAll(`{${name}}`, encodeURIComponent(String(value)));
  }
  if (missing.length > 0) {
    throw new Error(`Missing params: ${missing.join(", ")} for source '${ds.namespace}'`);
  }

  // 2. Param thừa (không có placeholder) → query string
  const extraParams = Object.entries(params)
    .filter(([k]) => !ds.requiredParams.includes(k) && !ds.resourcePath!.includes(`{${k}}`));
  const qs = new URLSearchParams();
  for (const [k, v] of extraParams) qs.append(k, String(v));

  // 3. Kind=Single → tự append mode=single (BE consumer dùng cờ này)
  if (ds.kind === "Single") qs.append("mode", "single");

  const url = `${ds.baseUrl}${path}`;
  return qs.toString() ? `${url}?${qs}` : url;
}
```

### Worked example — `finance.monthly.row`

```ts
const ds: DataSourceDto = {
  namespace:      "finance",
  serviceId:      "lakehouse",
  resourcePath:   "/lakehouse/contracts/{contractCode}/prefill",
  requiredParams: ["contractCode"],
  baseUrl:        "http://lakehouseservice:8080",  // Docker internal — qua nginx khi từ browser
  kind:           "Single",
  operationId:    "lakehouse::prefill",
  schemaPath:     "/lakehouse/contracts/{contractCode}/schema",
};

const params = {
  contractCode: "finance.monthly.row",
  source:       "demo",
  year:         2026,
  month:        6,
  department:   1,
};

const url = buildDataSourceUrl(ds, params);
// → "http://lakehouseservice:8080/lakehouse/contracts/finance.monthly.row/prefill?source=demo&year=2026&month=6&department=1&mode=single"
```

> **Browser thực tế**: thay `http://lakehouseservice:8080` (Docker hostname) bằng nginx public URL. Pattern thường là:
> ```ts
> const publicUrl = ds.baseUrl
>   .replace("http://lakehouseservice:8080", import.meta.env.VITE_API_BASE);
> // hoặc làm proxy /lakehouse/* ở nginx — tùy infra
> ```

---

## 4. Fetch + parse response

### Kind=Single → response có `single` object phẳng

```ts
interface FormPrefillResult {
  contractCode: string;
  rowCount: number;
  rows: Record<string, unknown>[];     // luôn có (multi-row)
  single: Record<string, unknown> | null;  // chỉ có khi ?mode=single
}

const res = await fetch(url, {
  headers: { Authorization: `Bearer ${token}` },
});
const data: FormPrefillResult = await res.json();

// data.single = {
//   year: 2026,
//   month: 6,
//   yearMonth: "2026-06",
//   departmentId: 1,
//   departmentName: "Khoa Tim mạch",
//   totalRevenue: 765000000,
//   totalCost: 420750000,
//   netProfit: 344250000,
//   patientCount: 1020,
//   avgRevenuePerPatient: 750000
// }
```

### Kind=List → response trả `rows` array

FE iterate `rows` → render bảng. Expression `{{sources.X.field}}` không áp dụng (vì không có 1 record cụ thể). Thường dùng cho `Table`/`Repeater` widget.

---

## 5. Build source map cho expression resolver

Sau khi fetch tất cả DataSource → gom thành 1 map theo `namespace`:

```ts
type SourceMap = Record<string, Record<string, unknown>>;

async function loadSources(
  layout: ScreenLayoutDto,
  paramsByNamespace: Record<string, Record<string, string | number>>
): Promise<SourceMap> {
  const sources: SourceMap = {};

  await Promise.all(
    layout.dataSources.map(async (ds) => {
      const params = paramsByNamespace[ds.namespace] ?? {};
      try {
        const url = buildDataSourceUrl(ds, params);
        const res = await fetch(url, { headers: { Authorization: `Bearer ${token}` }});
        if (!res.ok) {
          console.warn(`DataSource ${ds.namespace} HTTP ${res.status}`);
          return;
        }
        const data = await res.json();
        // Single → dùng data.single; List → dùng data.rows (caller tự decide)
        sources[ds.namespace] = ds.kind === "Single" ? (data.single ?? {}) : data;
      } catch (e) {
        console.warn(`DataSource ${ds.namespace} fetch failed:`, e);
      }
    })
  );

  return sources;
}
```

---

## 6. Resolve `{{sources.X.field}}` expression

```ts
const EXPR_PATTERN = /\{\{sources\.([a-zA-Z0-9_-]+)\.([a-zA-Z0-9_.-]+)\}\}/g;

function resolveExpression(expr: string, sources: SourceMap): unknown {
  // Trường hợp 1: toàn bộ expr là 1 placeholder → trả raw value (giữ kiểu)
  const fullMatch = expr.match(/^\{\{sources\.([a-zA-Z0-9_-]+)\.([a-zA-Z0-9_.-]+)\}\}$/);
  if (fullMatch) {
    const [, ns, path] = fullMatch;
    return getPath(sources[ns], path);
  }

  // Trường hợp 2: expr có nhiều placeholder hoặc lẫn text → string-interpolate
  return expr.replace(EXPR_PATTERN, (_, ns: string, path: string) => {
    const v = getPath(sources[ns], path);
    return v == null ? "" : String(v);
  });
}

function getPath(obj: unknown, path: string): unknown {
  if (obj == null) return undefined;
  return path.split(".").reduce<any>((acc, key) => acc?.[key], obj);
}
```

---

## 7. Apply `displayFormat` (BE chỉ là hint)

BE trả raw value. FE format hiển thị dựa trên `displayFormat`:

```ts
function formatValue(value: unknown, format: string | null): string {
  if (value == null) return "";
  if (!format) return String(value);

  const [kind, ...args] = format.split(":");
  switch (kind) {
    case "date": {
      // "date:DD/MM/YYYY"
      const pattern = args.join(":") || "YYYY-MM-DD";
      return dayjs(String(value)).format(pattern);
    }
    case "currency": {
      // "currency:VND"
      const cur = args[0] ?? "VND";
      return new Intl.NumberFormat("vi-VN", { style: "currency", currency: cur }).format(Number(value));
    }
    case "number": {
      return new Intl.NumberFormat("vi-VN").format(Number(value));
    }
    case "percent": {
      return `${Number(value).toFixed(args[0] ? Number(args[0]) : 1)}%`;
    }
    default:
      return String(value);
  }
}
```

---

## 8. Render flow tổng

```ts
async function renderScreen(moduleCode: string, screenCode: string, routeParams: URLSearchParams) {
  // 1. Layout
  const layout: ScreenLayoutDto = await fetch(
    `/forms/screens/${moduleCode}/${screenCode}/layout`,
    { headers: { Authorization: `Bearer ${token}` }}
  ).then(r => r.json());

  // 2. Params per namespace
  // Chiến lược đơn giản: TẤT CẢ DataSource cùng dùng URL query string làm nguồn param.
  // Phức tạp hơn: per-namespace mapping nếu cần.
  const baseParams = Object.fromEntries(routeParams.entries());
  const paramsByNs = Object.fromEntries(
    layout.dataSources.map(ds => [ds.namespace, baseParams])
  );

  // 3. Fetch all DataSources
  const sources = await loadSources(layout, paramsByNs);

  // 4. Render tabs + widgets
  for (const tab of layout.tabs) {
    for (const widget of tab.widgets) {
      if (widget.widgetType === "FormSection" && widget.formSchema) {
        // 5. Resolve field bindings
        for (const field of widget.formSchema.fields) {
          if (!field.dataBinding) continue;
          const raw = resolveExpression(field.dataBinding.expression, sources);
          field.value = formatValue(raw, field.dataBinding.displayFormat);
        }
        renderFormSection(widget);
      } else if (widget.widgetType === "ChartEmbed") {
        renderChartEmbed(widget);  // xem section 9
      }
      // ... các widget khác
    }
  }
}
```

---

## 9. Chart widget — gọi thẳng endpoint, không qua DataSource

`ChartEmbed` widget có `config.contractCode` (hoặc tương tự — config tùy widget admin set). FE gọi:

```ts
async function renderChartEmbed(widget: ScreenLayoutWidgetDto) {
  const cfg = widget.config as { contractCode: string; year?: number; department?: number };
  const qs = new URLSearchParams();
  qs.append("source", "demo");                          // hoặc bỏ → SQL source
  if (cfg.year)       qs.append("year",       String(cfg.year));
  if (cfg.department) qs.append("department", String(cfg.department));

  const url = `/lakehouse/contracts/${cfg.contractCode}/chart?${qs}`;
  // → /lakehouse/contracts/finance.monthly.row/chart?source=demo&year=2026

  const sduiPage: SduiPage = await fetch(url, {
    headers: { Authorization: `Bearer ${token}` }
  }).then(r => r.json());

  // FE đã có SduiPage renderer (giữ nguyên từ doc 52 / chart cũ).
  renderSduiPage(sduiPage);
}
```

### SDUI shape (cần renderer biết các component)

```ts
type SduiComponent =
  | { type: "KpiCard";      span: number; props: KpiCardProps }
  | { type: "ProgressList"; span: number; props: ProgressListProps }
  | { type: "AlertList";    span: number; props: AlertListProps }
  | { type: "FlowPipeline"; span: number; props: FlowPipelineProps }
  | { type: "ChartPie";     span: number; props: ChartPieProps };

interface SduiPage {
  code: string;        // "finance-monthly"
  title: string;
  badge: string;
  live: boolean;
  subtitle: string;
  actions: Array<{ label: string; variant: string; href: string | null }>;
  rows: Array<{ components: SduiComponent[] }>;
  generatedAt: string;
}
```

---

## 10. Worked example end-to-end — `finance.monthly.row`

### 10.1. Admin tạo screen

Admin POST screen + DataSource (xem doc 59 §C). Không cần truyền giá trị `contractCode` — chỉ khai báo là cần.

### 10.2. FE route: `/forms/finance/monthly-report?contractCode=finance.monthly.row&year=2026&month=6&department=1&source=demo`

```ts
const layout = await api.get("/forms/screens/finance/monthly-report/layout");

// layout.dataSources[0]:
// {
//   namespace: "finance",
//   resourcePath: "/lakehouse/contracts/{contractCode}/prefill",
//   requiredParams: ["contractCode"],
//   baseUrl: "http://lakehouseservice:8080",
//   kind: "Single", ...
// }

const params = {
  contractCode: "finance.monthly.row",
  source: "demo",
  year: 2026, month: 6, department: 1,
};

const url = buildDataSourceUrl(layout.dataSources[0], params);
// → ".../prefill?source=demo&year=2026&month=6&department=1&mode=single"

const { single } = await api.get(url);
// single = { year: 2026, month: 6, departmentName: "Khoa Tim mạch", totalRevenue: 765000000, ... }

const sources = { finance: single };

// Field "departmentName" có binding {{sources.finance.departmentName}}
// → resolveExpression → "Khoa Tim mạch"

// Field "totalRevenue" có binding {{sources.finance.totalRevenue}} + displayFormat "currency:VND"
// → resolveExpression → 765000000
// → formatValue → "765.000.000 ₫"
```

### 10.3. Render output

```
┌─ BC tài chính tháng 6/2026 ─────────────────────────────┐
│  Khoa: Khoa Tim mạch                                    │
│  Năm/Tháng: 2026-06                                     │
│  Doanh thu: 765.000.000 ₫                               │
│  Chi phí:   420.750.000 ₫                               │
│  Lợi nhuận: 344.250.000 ₫    (Biên: 45%)                │
│  Lượt khám: 1.020            (AVG: 750.000 ₫/lượt)      │
└─────────────────────────────────────────────────────────┘
```

---

## 11. Error handling — 5 case FE phải xử lý

| Case | Triệu chứng | FE làm gì |
|------|------------|----------|
| Provider inactive | `baseUrl: null` trong DataSourceDto | Skip source, render placeholder "Dữ liệu tạm offline" |
| Operation inactive | Cùng triệu chứng (BE fallback null) | Same |
| Param thiếu | `requiredParams` có tên mà FE không cung cấp được | `buildDataSourceUrl` throw — show form prompt user nhập |
| BE 404 / 5xx | Fetch fail | Console warn + fallback empty source `{}` (expression resolve thành "") |
| Single = null | Query không match row nào | FE check `data.single == null` → "Không có dữ liệu" |

```ts
// Robust resolver
function safeResolve(expr: string, sources: SourceMap, fallback = ""): string {
  try {
    const v = resolveExpression(expr, sources);
    return v == null ? fallback : String(v);
  } catch {
    return fallback;
  }
}
```

---

## 12. Checklist FE deploy

- [ ] `buildDataSourceUrl` xử lý `{name}` placeholder + `kind=Single` → `?mode=single`
- [ ] `loadSources` chạy parallel, error 1 source không kill toàn page
- [ ] `resolveExpression` support full-match (giữ kiểu) + interpolation
- [ ] `formatValue` support: `date:*`, `currency:*`, `number`, `percent`
- [ ] Component SDUI: `KpiCard`, `ProgressList`, `AlertList`, `FlowPipeline`, `ChartPie` (giữ nguyên renderer cũ doc 52)
- [ ] Inactive source → placeholder, không crash
- [ ] ENV `VITE_API_BASE` để swap `lakehouseservice:8080` → public URL khi build prod
- [ ] Cache `/forms/screens/.../layout` (lifetime ~30s, screen ít đổi); KHÔNG cache `/lakehouse/contracts/.../prefill` (data thay đổi)

---

## 13. Quick API reference

| Endpoint | Method | Mục đích |
|---------|--------|----------|
| `/forms/screens/{m}/{c}/layout` | GET | Lấy layout + dataSources resolved |
| `{baseUrl}/lakehouse/contracts/{code}/prefill?mode=single&<params>` | GET | Single object cho form bind |
| `{baseUrl}/lakehouse/contracts/{code}/prefill?<params>` | GET | Multi-row cho bảng |
| `{baseUrl}/lakehouse/contracts/{code}/chart?<params>` | GET | SduiPage JSON |
| `{baseUrl}/lakehouse/contracts/{code}/schema` | GET | (Phase 2 — chưa implement) Field metadata reflection |
| `/forms/admin/providers/{code}/operations` | GET | Liệt kê op admin (UI catalog) |

Bearer token bắt buộc cho mọi call (JWT từ AuthService).
