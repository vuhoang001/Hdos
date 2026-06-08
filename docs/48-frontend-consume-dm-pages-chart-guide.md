# 48 — Frontend Guide: Consume `/dm/pages/{code}` để render chart

> **Mục đích.** Hướng dẫn FE developer lấy dữ liệu **đã pre-aggregate, chart-ready**
> từ `DataMatchingService` qua endpoint `GET /dm/pages/{code}` (SDUI engine) và
> render thành KPI cards + chart + bảng. Toàn bộ data flow đi từ **lakehouse view
> → with-auto-profile → sync → StagingRecord → SduiEngine → FE**.
>
> Tài liệu này **doc-only** — không thay đổi code BE. Sử dụng infra đã có sẵn
> (`SduiEngine`, `PagesController`) cùng config mẫu `ExecutiveSduiConfig`.
>
> **Tài liệu liên quan:**
> - [`docs/25-sdui-server-driven-ui.md`](./25-sdui-server-driven-ui.md) — khái niệm SDUI vs Dashboard
> - [`docs/38-frontend-sdui-implementation-guide.md`](./38-frontend-sdui-implementation-guide.md) — FE generic SDUI render (cho DynamicForm)
> - [`docs/44-unified-ingest-pipeline.md`](./44-unified-ingest-pipeline.md) — luồng data tổng
> - [`docs/45-lakehouse-auto-sourceprofile.md`](./45-lakehouse-auto-sourceprofile.md) — MVP B với-auto-profile
> - [`docs/46-playbook-add-source-data.md`](./46-playbook-add-source-data.md) — playbook onboard nguồn data
> - [`docs/47-test-mvp-b-lakehouse-view.md`](./47-test-mvp-b-lakehouse-view.md) — test guide

---

## 0. TL;DR

```bash
# Liệt kê page codes có sẵn
curl -s http://localhost:5000/dm/pages | jq '.data'
# → ["executive"]

# Lấy 1 page đầy đủ (data + layout)
curl -s "http://localhost:5000/dm/pages/executive?date=2026-06-08" | jq '.data'
```

**FE render trong 5 dòng:**
```tsx
const { data: page } = await fetch('/dm/pages/executive').then(r => r.json());
return <SduiPageView page={page} />;
```

→ `<SduiPageView>` switch theo `component.type` (`KpiCard` | `ProgressList` | `AlertList` | `FlowPipeline` | `ChartPie`), render từng widget. FE **không cần biết logic aggregation** — BE đã làm hết.

---

## 1. Đối tượng đọc & phạm vi

| | |
|---|---|
| **Đọc cho ai?** | FE developer (Next.js / React / Vue) cần hiển thị chart từ data đã ingest qua `with-auto-profile` + `sync` |
| **Cần biết trước** | Đọc qua doc [25 §1-2](./25-sdui-server-driven-ui.md) (khái niệm SDUI) và [44 §1-3](./44-unified-ingest-pipeline.md) (pipeline ingest). Không cần đọc hết. |
| **Stack giả định** | Next.js 14 + TypeScript + TailwindCSS + Recharts. Có thể adapt sang Vue/Svelte (logic chung). |
| **KHÔNG nằm trong scope** | Cách thêm chart mới (thuộc BE — xem §10), authentication setup (đã cover ở doc 38 §6), test E2E |

---

## 2. Quick start (5 phút)

### 2.1 Smoke test bằng curl

```bash
BASE=http://localhost:5000

# [1] List codes
curl -s "$BASE/dm/pages" | jq
# {
#   "success": true,
#   "data": ["executive"],
#   "error": null
# }

# [2] Render page "executive"
curl -s "$BASE/dm/pages/executive?date=2026-06-08" | jq '.data | {code, title, rowCount: (.rows|length)}'
# {
#   "code":     "executive",
#   "title":    "Bảng điều hành bệnh viện",
#   "rowCount": 3
# }
```

Nếu trả 200 với `rows[]` có nội dung → infra OK, sang bước FE.

### 2.2 React snippet đầu tiên

```tsx
// app/dashboard/page.tsx
import { SduiPageView } from '@/components/sdui/SduiPageView';

export default async function Dashboard() {
  const res = await fetch('http://localhost:5000/dm/pages/executive', {
    cache: 'no-store',
  });
  const json = await res.json();
  if (!json.success) throw new Error(json.error?.message ?? 'Failed');

  return (
    <main className="p-6 bg-slate-50 min-h-screen">
      <SduiPageView page={json.data} />
    </main>
  );
}
```

`<SduiPageView>` ở §7.3 — copy nguyên đoạn dưới về là chạy được.

---

## 3. Architecture context (1 sơ đồ)

```
[Postgres warehouse]                       [Hdos cluster]
api.bed_occupancy                          ┌──────────────────────────┐
       │                                   │ LakehouseService         │
       │ POST /lakehouse/view-bindings/    │  ▸ ViewBinding            │
       │       with-auto-profile           │  ▸ WarehouseViewSyncer    │
       ▼                                   │                          │
ViewBinding registered    ────publish─►    │ RabbitMQ                 │
                                           │  RawRecordIngestRequested │
                                           │           │              │
                                           │           ▼              │
                                           │ DataMatchingService      │
                                           │  ▸ Consumer apply         │
                                           │    SourceProfile mappings │
                                           │  ▸ StagingRecord (canonical) │
                                           │                          │
                                           │ ┌────────────────┐       │
                            GET /dm/pages/ │ │  SduiEngine     │       │
                            executive   ◄──┤ │  (this doc)     │       │
                                           │ │ aggregate + chart│      │
                                           │ └────────────────┘       │
                                           └──────────────────────────┘
                                                       ▲
                                                       │ FE consumer
                                                       │ (this doc)
```

**Key insight:** `/dm/records` trả raw row-by-row (không chart-ready), `/dm/pages/{code}` trả **layout + chart payload đã pre-compute**. FE chỉ render, không aggregate.

So sánh chi tiết: [doc 25 §1](./25-sdui-server-driven-ui.md#1-khái-niệm--so-sánh-với-dashboard-engine).

---

# PHẦN A — REFERENCE CATALOG

## 4. Endpoint catalog

Base URL: `http://localhost:5000` (qua nginx) hoặc `http://datamatchingservice:8080` (trong Docker network).

### 4.1 `GET /dm/pages` — list page codes

Liệt kê tất cả SDUI page đã đăng ký (config bằng C# class kế thừa `SduiPageConfig`).

**Request:**
```
GET /dm/pages HTTP/1.1
```

Không có query/path/body params.

**Response 200:**
```json
{
  "success": true,
  "data": ["executive"],
  "error": null
}
```

### 4.2 `GET /dm/pages/{code}` — render 1 page

Render đầy đủ layout + data cho 1 page.

**Path param:**
| Param | Type | Required | Mô tả |
|---|---|---|---|
| `code` | string | ✓ | Page code lấy từ `GET /dm/pages` (vd `"executive"`) |

**Query params (optional):**
| Param | Type | Default | Mô tả |
|---|---|---|---|
| `sourceSystem` | string | `null` (tất cả) | Lọc record theo source system. VD `"his-01"`, `"lakehouse:bed_occupancy"` |
| `date` | `yyyy-MM-dd` | hôm nay (UTC) | Ngày báo cáo |

**Response 200:** xem §5 (full shape).

**Response 404:**
```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "PAGE.NOT_FOUND",
    "message": "Page 'foo' không tồn tại. Dùng GET /dm/pages để xem danh sách."
  }
}
```

### 4.3 Auth

Hiện tại `RecordsController` và `PagesController` **đang comment `[Authorize]`** ra (xem `RecordsController.cs:16`) — gọi không cần token cũng được. **Production sẽ enable** — FE chuẩn bị sẵn header:

```http
Authorization: Bearer <JWT>
```

Lấy JWT từ `POST /auth/login`. Chi tiết: [doc 38 §6](./38-frontend-sdui-implementation-guide.md#6-authentication).

---

## 5. Response shape — `SduiPage`

### 5.1 Top-level

```jsonc
{
  "success": true,
  "data": {
    "code":        "executive",
    "title":       "Bảng điều hành bệnh viện",
    "badge":       "Trực tiếp",          // hiển thị nhãn nhỏ cạnh title
    "live":        true,                  // FE bật auto-refresh khi true
    "subtitle":    "Cập nhật: 14:32 · Ngày 08/06/2026",
    "actions": [                          // toolbar buttons
      { "label": "Xuất PDF", "variant": "default", "color": null },
      { "label": "Cài đặt",  "variant": "default", "color": null }
    ],
    "rows": [                             // 12-col grid layout
      { "components": [ /* ... */ ] },
      { "components": [ /* ... */ ] },
      { "components": [ /* ... */ ] }
    ],
    "generatedAt": "2026-06-08T07:32:11.123Z"
  },
  "error": null
}
```

| Field | Type | Note |
|---|---|---|
| `code` | string | echo lại path param |
| `title` | string | Tiêu đề lớn |
| `badge` | string \| null | Nhãn hiển thị cạnh title (vd "Trực tiếp"). `null` = ẩn |
| `live` | bool | `true` ⇒ FE nên polling refresh (vd 30s/lần) |
| `subtitle` | string \| null | Phụ đề (vd "Cập nhật: HH:mm") |
| `actions` | `SduiAction[]` | Buttons toolbar |
| `rows` | `SduiRow[]` | Danh sách rows, mỗi row là 1 grid 12-col chứa components |
| `generatedAt` | ISO 8601 UTC | Server timestamp |

### 5.2 `SduiAction`

```ts
interface SduiAction {
  label:   string;
  variant: 'primary' | 'default' | 'danger';
  color:   string | null;   // hex như "#1677ff" hoặc null
}
```

### 5.3 `SduiRow` & grid

```ts
interface SduiRow {
  components: SduiComponent[];   // mỗi component có .span (tổng row ≤ 24)
}
```

Mỗi `SduiComponent` có field `span` (kiểu `number | null`) — số ô chiếm trên grid. Convention codebase dùng: tổng span trong 1 row = **24** (4 KpiCard span=6, hoặc 2 component span=12, v.v.).

`span = null` → FE coi như full-width (24).

### 5.4 5 `SduiComponent` types (polymorphic)

Discriminator field: `"type"`. 5 giá trị:

| `type` | Component | Khi nào dùng |
|---|---|---|
| `KpiCard` | Số liệu dạng card lớn | Stat tổng hợp |
| `ProgressList` | Danh sách thanh tiến trình | Tỷ lệ theo nhóm (vd công suất giường theo khoa) |
| `AlertList` | Danh sách cảnh báo có severity | Cảnh báo lâm sàng / system alert |
| `FlowPipeline` | Stages dòng chảy | State machine visualization (vd dòng bệnh nhân) |
| `ChartPie` | Biểu đồ tròn/donut | Phân bổ tỷ lệ |

Chi tiết mỗi type ở §6.

---

## 6. 5 component types — full schema + sample

> **Lưu ý serialization:** props nested camelCase (ASP.NET Core 8 default `JsonNamingPolicy.CamelCase`). VD C# `KpiCardProps.Accent` → JSON `accent`. Tham khảo `SduiComponent.cs:13`.

### 6.1 `KpiCard`

**Khi nào:** số liệu dạng "tổng X = Y" với màu accent + hint phụ.

**Shape JSON:**
```jsonc
{
  "type": "KpiCard",
  "span": 6,
  "props": {
    "title":     "Tổng bệnh nhân nội trú",
    "value":     150,            // int hoặc string ("4.23 tỷ")
    "accent":    "#1677ff",       // null = default; #ff4d4f danger; #faad14 warn; #52c41a success
    "hint":      "đang điều trị",
    "hintColor": null              // hex hoặc null
  }
}
```

**Rendering hint:** card có border-top màu `accent`, title nhỏ trên, value lớn ở giữa, hint nhỏ dưới.

### 6.2 `ProgressList`

**Khi nào:** danh sách item với thanh progress bar (vd công suất từng khoa).

**Shape JSON:**
```jsonc
{
  "type": "ProgressList",
  "span": 8,
  "props": {
    "title":         "Công suất giường theo khoa",
    "headerAction":  "Xem chi tiết",         // link/button bên phải header, null = ẩn
    "maxValue":      100,                     // scale của progress bar
    "items": [
      {
        "label":          "Tim mạch (15/20)",
        "value":          75,                  // current
        "secondaryValue": 90,                  // ngưỡng cảnh báo, null = ẩn
        "color":          "#52c41a"            // null = default
      },
      {
        "label":          "ICU (18/20)",
        "value":          90,
        "secondaryValue": 90,
        "color":          "#faad14"
      }
    ],
    "footerActions": [                          // null = ẩn footer
      { "label": "Tải báo cáo", "variant": "default" }
    ]
  }
}
```

**Rendering hint:** Mỗi item: label trên, bar dưới chiếm `(value / maxValue) * 100%`. Vẽ vạch dọc tại `secondaryValue` làm reference line.

### 6.3 `AlertList`

**Khi nào:** danh sách cảnh báo có severity, time, patient/dept context.

**Shape JSON:**
```jsonc
{
  "type": "AlertList",
  "span": 16,
  "props": {
    "title":         "Cảnh báo lâm sàng",
    "realtimeBadge": true,           // hiển thị dot pulsing
    "maxHeight":     400,             // px cho scroll container, null = không scroll
    "totalCount":    23,              // tổng (có thể > items.length nếu paged)
    "items": [
      {
        "code":     "Troponin I",     // mã chỉ số / ICD
        "text":     "Tăng cao bất thường — cần can thiệp",
        "patient":  "Nguyễn Văn A",
        "dept":     "ICU",
        "time":     "3 phút trước",
        "severity": "critical"         // "critical" | "warning" | "info"
      }
    ]
  }
}
```

**Rendering hint:** severity → icon + border color (critical=đỏ, warning=vàng, info=xanh). Scroll container `maxHeight`.

### 6.4 `FlowPipeline`

**Khi nào:** visualize state machine theo chiều ngang (stages có value số).

**Shape JSON:**
```jsonc
{
  "type": "FlowPipeline",
  "span": 12,
  "props": {
    "title":  "Dòng bệnh nhân",
    "footer": "Tổng: 245 lượt",
    "stages": [
      { "label": "Chờ khám sàng", "value": 12, "color": "#1677ff" },
      { "label": "Đang nội trú",  "value": 150, "color": "#52c41a" },
      { "label": "Chờ xuất viện",  "value": 8,   "color": "#faad14" },
      { "label": "Đã xuất viện",   "value": 75,  "color": "#8c8c8c" }
    ]
  }
}
```

**Rendering hint:** chuỗi stage liên kết bằng mũi tên `→`, mỗi stage là 1 box màu `color` chứa label + value.

### 6.5 `ChartPie`

**Khi nào:** biểu đồ tròn / donut phân bổ tỷ lệ.

**Shape JSON:**
```jsonc
{
  "type": "ChartPie",
  "span": 12,
  "props": {
    "title":   "Đối tượng KCB",
    "height":  260,           // px
    "variant": "donut",        // "pie" | "donut"
    "legend":  true,
    "data": [
      { "label": "BHYT",     "value": 120 },
      { "label": "Dịch vụ", "value": 60  },
      { "label": "Khác",    "value": 20  }
    ],
    "colors":  ["#1677ff", "#52c41a", "#faad14", "#ff4d4f", "#722ed1"]
  }
}
```

**Rendering hint:** dùng Recharts `<PieChart>` (`<Pie>` với `innerRadius=0` cho pie, `innerRadius=60` cho donut).

---

## 7. TypeScript types (copy-paste-được)

```ts
// src/types/sdui.ts

export type SduiActionVariant = 'primary' | 'default' | 'danger';

export interface SduiAction {
  label:   string;
  variant: SduiActionVariant;
  color:   string | null;
}

export interface SduiRow {
  components: SduiComponent[];
}

export interface SduiPage {
  code:        string;
  title:       string;
  badge:       string | null;
  live:        boolean;
  subtitle:    string | null;
  actions:     SduiAction[];
  rows:        SduiRow[];
  generatedAt: string;          // ISO 8601 UTC
}

// ─── Component union (discriminator: "type") ──────────────────────

export type SduiComponent =
  | KpiCardComponent
  | ProgressListComponent
  | AlertListComponent
  | FlowPipelineComponent
  | ChartPieComponent;

interface BaseComponent {
  span: number | null;          // 1..24, null = full width
}

// KpiCard
export interface KpiCardProps {
  title:     string;
  value:     number | string;
  accent:    string | null;     // hex
  hint:      string | null;
  hintColor: string | null;
}
export interface KpiCardComponent extends BaseComponent {
  type:  'KpiCard';
  props: KpiCardProps;
}

// ProgressList
export interface ProgressItem {
  label:          string;
  value:          number;
  secondaryValue: number | null;
  color:          string | null;
}
export interface FooterAction {
  label:   string;
  variant: string;
}
export interface ProgressListProps {
  title:         string;
  headerAction:  string | null;
  maxValue:      number;
  items:         ProgressItem[];
  footerActions: FooterAction[] | null;
}
export interface ProgressListComponent extends BaseComponent {
  type:  'ProgressList';
  props: ProgressListProps;
}

// AlertList
export type AlertSeverity = 'critical' | 'warning' | 'info';
export interface AlertItem {
  code:     string;
  text:     string;
  patient:  string;
  dept:     string;
  time:     string;
  severity: AlertSeverity;
}
export interface AlertListProps {
  title:         string;
  realtimeBadge: boolean;
  maxHeight:     number | null;
  totalCount:    number;
  items:         AlertItem[];
}
export interface AlertListComponent extends BaseComponent {
  type:  'AlertList';
  props: AlertListProps;
}

// FlowPipeline
export interface FlowStage {
  label: string;
  value: number;
  color: string | null;
}
export interface FlowPipelineProps {
  title:  string;
  footer: string | null;
  stages: FlowStage[];
}
export interface FlowPipelineComponent extends BaseComponent {
  type:  'FlowPipeline';
  props: FlowPipelineProps;
}

// ChartPie
export interface ChartPieDataPoint {
  label: string;
  value: number;
}
export interface ChartPieProps {
  title:   string;
  height:  number | null;
  variant: 'pie' | 'donut' | null;
  legend:  boolean;
  data:    ChartPieDataPoint[];
  colors:  string[] | null;
}
export interface ChartPieComponent extends BaseComponent {
  type:  'ChartPie';
  props: ChartPieProps;
}

// API envelope
export interface ApiResponse<T> {
  success: boolean;
  data:    T | null;
  error:   { code: string; message: string } | null;
}
```

---

# PHẦN B — HANDS-ON FE IMPLEMENTATION

## 8. Setup (Next.js 14 + Recharts)

### 8.1 Install

```bash
pnpm add recharts clsx
pnpm add -D @types/node
```

### 8.2 Env

```bash
# .env.local
NEXT_PUBLIC_DM_BASE_URL=http://localhost:5000
```

### 8.3 Fetch wrapper (type-safe)

```ts
// src/lib/dmClient.ts
import type { ApiResponse, SduiPage } from '@/types/sdui';

const BASE = process.env.NEXT_PUBLIC_DM_BASE_URL ?? 'http://localhost:5000';

export async function fetchDmPage(
  code: string,
  opts: { sourceSystem?: string; date?: string; signal?: AbortSignal } = {},
): Promise<SduiPage> {
  const qs = new URLSearchParams();
  if (opts.sourceSystem) qs.set('sourceSystem', opts.sourceSystem);
  if (opts.date)         qs.set('date', opts.date);
  const url = `${BASE}/dm/pages/${encodeURIComponent(code)}${qs.size ? `?${qs}` : ''}`;

  const res  = await fetch(url, { signal: opts.signal, cache: 'no-store' });
  const json = (await res.json()) as ApiResponse<SduiPage>;
  if (!json.success || !json.data)
    throw new Error(json.error?.message ?? `Fetch /dm/pages/${code} failed (HTTP ${res.status})`);
  return json.data;
}

export async function listDmPages(): Promise<string[]> {
  const res  = await fetch(`${BASE}/dm/pages`);
  const json = (await res.json()) as ApiResponse<string[]>;
  if (!json.success || !json.data) throw new Error(json.error?.message ?? 'List pages failed');
  return json.data;
}
```

---

## 9. Page wrapper

```tsx
// src/components/sdui/SduiPageView.tsx
import type { SduiPage } from '@/types/sdui';
import { SduiRowGrid } from './SduiRowGrid';

export function SduiPageView({ page }: { page: SduiPage }) {
  return (
    <div className="space-y-4">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-3">
            {page.title}
            {page.badge && (
              <span className="text-xs px-2 py-1 rounded bg-red-100 text-red-700 flex items-center gap-1">
                {page.live && <span className="w-2 h-2 rounded-full bg-red-500 animate-pulse" />}
                {page.badge}
              </span>
            )}
          </h1>
          {page.subtitle && <p className="text-sm text-slate-500">{page.subtitle}</p>}
        </div>
        <div className="flex gap-2">
          {page.actions.map(a => (
            <button
              key={a.label}
              className={
                a.variant === 'primary' ? 'px-3 py-1.5 bg-blue-600 text-white rounded' :
                a.variant === 'danger'  ? 'px-3 py-1.5 bg-red-600  text-white rounded' :
                                          'px-3 py-1.5 border rounded text-slate-700'
              }
            >
              {a.label}
            </button>
          ))}
        </div>
      </header>

      <div className="space-y-4">
        {page.rows.map((row, i) => <SduiRowGrid key={i} row={row} />)}
      </div>
    </div>
  );
}
```

---

## 10. Row grid (12-col, span)

```tsx
// src/components/sdui/SduiRowGrid.tsx
import type { SduiRow } from '@/types/sdui';
import { ComponentRenderer } from './ComponentRenderer';

export function SduiRowGrid({ row }: { row: SduiRow }) {
  return (
    <div className="grid grid-cols-24 gap-4">
      {row.components.map((c, i) => (
        <div
          key={i}
          style={{ gridColumn: `span ${c.span ?? 24}` }}
        >
          <ComponentRenderer component={c} />
        </div>
      ))}
    </div>
  );
}
```

> **Tailwind note:** `grid-cols-24` không có sẵn — thêm vào `tailwind.config.ts`:
> ```ts
> theme: { extend: { gridTemplateColumns: { 24: 'repeat(24, minmax(0, 1fr))' } } }
> ```

---

## 11. Per-component renderer (switch theo `type`)

```tsx
// src/components/sdui/ComponentRenderer.tsx
import type { SduiComponent } from '@/types/sdui';
import { KpiCard }      from './widgets/KpiCard';
import { ProgressList } from './widgets/ProgressList';
import { AlertList }    from './widgets/AlertList';
import { FlowPipeline } from './widgets/FlowPipeline';
import { ChartPie }     from './widgets/ChartPie';

export function ComponentRenderer({ component }: { component: SduiComponent }) {
  switch (component.type) {
    case 'KpiCard':      return <KpiCard      props={component.props} />;
    case 'ProgressList': return <ProgressList props={component.props} />;
    case 'AlertList':    return <AlertList    props={component.props} />;
    case 'FlowPipeline': return <FlowPipeline props={component.props} />;
    case 'ChartPie':     return <ChartPie     props={component.props} />;
    default:
      // exhaustive check — TS sẽ báo lỗi nếu thêm component type mới mà chưa handle
      const _exhaustive: never = component;
      return null;
  }
}
```

### 11.1 KpiCard widget

```tsx
// src/components/sdui/widgets/KpiCard.tsx
import type { KpiCardProps } from '@/types/sdui';

export function KpiCard({ props }: { props: KpiCardProps }) {
  return (
    <div
      className="bg-white rounded-lg shadow p-4 border-t-4"
      style={{ borderTopColor: props.accent ?? '#94a3b8' }}
    >
      <div className="text-xs text-slate-500 uppercase tracking-wide">{props.title}</div>
      <div className="text-3xl font-bold mt-2">{props.value}</div>
      {props.hint && (
        <div className="text-sm mt-1" style={{ color: props.hintColor ?? '#64748b' }}>
          {props.hint}
        </div>
      )}
    </div>
  );
}
```

### 11.2 ProgressList widget

```tsx
// src/components/sdui/widgets/ProgressList.tsx
import type { ProgressListProps } from '@/types/sdui';

export function ProgressList({ props }: { props: ProgressListProps }) {
  return (
    <div className="bg-white rounded-lg shadow p-4">
      <div className="flex items-center justify-between mb-3">
        <h3 className="font-semibold text-slate-700">{props.title}</h3>
        {props.headerAction && (
          <button className="text-xs text-blue-600 hover:underline">{props.headerAction}</button>
        )}
      </div>

      <div className="space-y-3">
        {props.items.map((item, i) => {
          const pct = Math.min(100, (item.value / props.maxValue) * 100);
          return (
            <div key={i}>
              <div className="flex justify-between text-sm mb-1">
                <span className="text-slate-700">{item.label}</span>
                <span className="text-slate-500">{item.value}</span>
              </div>
              <div className="h-2 bg-slate-100 rounded relative">
                <div
                  className="h-full rounded transition-all"
                  style={{ width: `${pct}%`, backgroundColor: item.color ?? '#1677ff' }}
                />
                {item.secondaryValue != null && (
                  <div
                    className="absolute top-0 h-full w-px bg-red-400"
                    style={{ left: `${(item.secondaryValue / props.maxValue) * 100}%` }}
                  />
                )}
              </div>
            </div>
          );
        })}
      </div>

      {props.footerActions && props.footerActions.length > 0 && (
        <div className="flex gap-2 mt-4 pt-3 border-t">
          {props.footerActions.map(a => (
            <button key={a.label} className="text-xs px-2 py-1 border rounded text-slate-600">
              {a.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
```

### 11.3 AlertList widget

```tsx
// src/components/sdui/widgets/AlertList.tsx
import type { AlertListProps, AlertSeverity } from '@/types/sdui';

const SEVERITY_COLOR: Record<AlertSeverity, string> = {
  critical: '#ff4d4f',
  warning:  '#faad14',
  info:     '#1677ff',
};

export function AlertList({ props }: { props: AlertListProps }) {
  return (
    <div className="bg-white rounded-lg shadow p-4">
      <div className="flex items-center justify-between mb-3">
        <h3 className="font-semibold text-slate-700 flex items-center gap-2">
          {props.title}
          {props.realtimeBadge && <span className="w-2 h-2 rounded-full bg-red-500 animate-pulse" />}
        </h3>
        <span className="text-xs text-slate-500">{props.totalCount} cảnh báo</span>
      </div>

      <div
        className="space-y-2 overflow-y-auto"
        style={{ maxHeight: props.maxHeight ?? 'none' }}
      >
        {props.items.map((a, i) => (
          <div
            key={i}
            className="p-3 rounded border-l-4 bg-slate-50"
            style={{ borderLeftColor: SEVERITY_COLOR[a.severity] }}
          >
            <div className="flex items-center justify-between">
              <span className="font-mono text-xs px-1.5 py-0.5 bg-slate-200 rounded">{a.code}</span>
              <span className="text-xs text-slate-500">{a.time}</span>
            </div>
            <div className="text-sm mt-1 text-slate-700">{a.text}</div>
            <div className="text-xs mt-1 text-slate-500">{a.patient} · {a.dept}</div>
          </div>
        ))}
      </div>
    </div>
  );
}
```

### 11.4 FlowPipeline widget

```tsx
// src/components/sdui/widgets/FlowPipeline.tsx
import type { FlowPipelineProps } from '@/types/sdui';

export function FlowPipeline({ props }: { props: FlowPipelineProps }) {
  return (
    <div className="bg-white rounded-lg shadow p-4">
      <h3 className="font-semibold text-slate-700 mb-4">{props.title}</h3>

      <div className="flex items-center gap-2 overflow-x-auto">
        {props.stages.map((s, i) => (
          <div key={i} className="flex items-center gap-2 shrink-0">
            <div
              className="px-3 py-2 rounded text-white text-sm min-w-24"
              style={{ backgroundColor: s.color ?? '#64748b' }}
            >
              <div className="text-xs opacity-80">{s.label}</div>
              <div className="text-lg font-bold">{s.value}</div>
            </div>
            {i < props.stages.length - 1 && <span className="text-slate-400">→</span>}
          </div>
        ))}
      </div>

      {props.footer && (
        <div className="text-xs text-slate-500 mt-3 text-right">{props.footer}</div>
      )}
    </div>
  );
}
```

### 11.5 ChartPie widget (Recharts)

```tsx
// src/components/sdui/widgets/ChartPie.tsx
'use client';
import type { ChartPieProps } from '@/types/sdui';
import { PieChart, Pie, Cell, Legend, Tooltip, ResponsiveContainer } from 'recharts';

const DEFAULT_COLORS = ['#1677ff', '#52c41a', '#faad14', '#ff4d4f', '#722ed1', '#13c2c2'];

export function ChartPie({ props }: { props: ChartPieProps }) {
  const colors = props.colors ?? DEFAULT_COLORS;
  const innerR = props.variant === 'donut' ? 60 : 0;

  return (
    <div className="bg-white rounded-lg shadow p-4">
      <h3 className="font-semibold text-slate-700 mb-3">{props.title}</h3>
      <ResponsiveContainer width="100%" height={props.height ?? 260}>
        <PieChart>
          <Pie
            data={props.data}
            dataKey="value"
            nameKey="label"
            innerRadius={innerR}
            outerRadius={100}
            label={({ label, percent }) => `${label} (${(percent! * 100).toFixed(0)}%)`}
          >
            {props.data.map((_, i) => (
              <Cell key={i} fill={colors[i % colors.length]} />
            ))}
          </Pie>
          <Tooltip />
          {props.legend && <Legend />}
        </PieChart>
      </ResponsiveContainer>
    </div>
  );
}
```

---

## 12. Auto-refresh khi `live=true`

```tsx
// src/components/sdui/SduiPageLive.tsx
'use client';
import { useEffect, useState } from 'react';
import { fetchDmPage } from '@/lib/dmClient';
import { SduiPageView } from './SduiPageView';
import type { SduiPage } from '@/types/sdui';

const REFRESH_MS = 30_000;   // 30s

export function SduiPageLive({ code, initial }: { code: string; initial: SduiPage }) {
  const [page, setPage] = useState<SduiPage>(initial);

  useEffect(() => {
    if (!page.live) return;
    const ctl = new AbortController();
    const id  = setInterval(() => {
      fetchDmPage(code, { signal: ctl.signal })
        .then(setPage)
        .catch(err => { if (err.name !== 'AbortError') console.error(err); });
    }, REFRESH_MS);

    return () => { ctl.abort(); clearInterval(id); };
  }, [code, page.live]);

  return <SduiPageView page={page} />;
}
```

Combine với SSR fetch (Next.js):
```tsx
// app/dashboard/page.tsx
import { fetchDmPage } from '@/lib/dmClient';
import { SduiPageLive } from '@/components/sdui/SduiPageLive';

export default async function Dashboard() {
  const initial = await fetchDmPage('executive');
  return <SduiPageLive code="executive" initial={initial} />;
}
```

---

## 13. Loading + error states

```tsx
// app/dashboard/loading.tsx
export default function Loading() {
  return <div className="p-6 text-slate-500">Đang tải dashboard…</div>;
}

// app/dashboard/error.tsx
'use client';
export default function Error({ error, reset }: { error: Error; reset: () => void }) {
  return (
    <div className="p-6">
      <h2 className="text-red-600 font-bold">Lỗi tải dashboard</h2>
      <pre className="text-xs mt-2 text-slate-600">{error.message}</pre>
      <button onClick={reset} className="mt-3 px-3 py-1 border rounded">Thử lại</button>
    </div>
  );
}
```

---

## 14. Pitfalls & gotchas

| Vấn đề | Triệu chứng | Cách xử lý |
|---|---|---|
| Quên discriminator `type` | TS không thu hẹp được union, runtime render sai | Luôn dùng `switch (component.type)` exhaustive — TS sẽ báo nếu BE thêm component mới |
| Field có `null` ở response | KpiCard `hint` mất hoặc `accent` không màu | TS interface đều mark `\| null` — guard bằng `??` |
| `span = null` | Component không xếp đúng grid | Default 24 (full width): `c.span ?? 24` |
| `date` không gửi | Server lấy hôm nay (UTC) — sai timezone VN | FE truyền `date=YYYY-MM-DD` theo timezone VN tính trước |
| `sourceSystem` không truyền | Aggregate qua tất cả nguồn | Production: nên truyền để filter, tránh data nhiễu |
| `live=true` polling nặng | Server CPU cao khi nhiều client | Throttle ở FE (30s+), hoặc chuyển sang SSE/SignalR (xem doc 14) |
| Date string từ BE | `"2026-06-08T07:32:11.123Z"` | Dùng `new Date(s).toLocaleString('vi-VN')`. **Đừng** parse bằng regex |
| `data: null` khi 404 | TS crash vì truy cập `.code` của null | Wrapper `fetchDmPage` đã throw — đừng bypass |
| Page code phân biệt hoa thường | `Executive` ≠ `executive` | BE dùng `OrdinalIgnoreCase` lookup nhưng tốt nhất gõ chuẩn |

---

## 15. Test checklist

### 15.1 BE smoke
```bash
BASE=http://localhost:5000

# [1] Health: list trả về có "executive"
curl -s "$BASE/dm/pages" | jq '.data | index("executive")'   # should not be null

# [2] Page render: rows[].components[] phải có ≥ 1 element
curl -s "$BASE/dm/pages/executive" | jq '.data.rows | map(.components | length) | add'   # > 0

# [3] Tất cả component có "type" hợp lệ
curl -s "$BASE/dm/pages/executive" \
  | jq '[.data.rows[].components[].type] | unique' 
# expected: ["AlertList","ChartPie","FlowPipeline","KpiCard","ProgressList"] (subset OK)

# [4] Filter theo source
curl -s "$BASE/dm/pages/executive?sourceSystem=his-01&date=2026-06-08" | jq '.data.code'
```

### 15.2 FE checklist

- [ ] Mount `<Dashboard />`, không có warning React về key
- [ ] Mỗi component type render đúng widget (mở Inspector kiểm `data-testid` nếu thêm)
- [ ] KpiCard accent đổi màu khi value thay đổi (test bằng mock với `accent="#ff4d4f"`)
- [ ] ProgressList: bar width = `(value/maxValue)*100%`, line đỏ ở `secondaryValue`
- [ ] AlertList scroll khi items > maxHeight
- [ ] ChartPie variant `"donut"` ra donut (innerRadius > 0)
- [ ] `live=true` → polling 30s đổi data (test bằng cách insert StagingRecord rồi đợi)
- [ ] Error state hiển thị khi đổi URL thành `/dm/pages/khong-ton-tai`
- [ ] Auth: khi BE enable `[Authorize]`, FE gửi `Authorization: Bearer ...`

### 15.3 Network tab kiểm

Mỗi request `/dm/pages/{code}`:
- Status 200
- Response `Content-Type: application/json`
- Body shape: `{ success, data: SduiPage, error: null }`
- Duration < 500ms (nếu > 2s → check DataMatching DB index)

---

## 16. Mở rộng — thêm chart cho data lakehouse mới

Hiện tại chỉ có `executive` page (dùng `benh-nhan-noi-tru` + `cau-hinh-giuong`). Để FE consume được chart từ data lakehouse như `bed-occupancy`, cần **BE thêm 1 file C#**:

> ⚠️ **Pitfall đã từng gặp (2026-06-08):** `SduiEngine.ExecuteAsync` ban đầu dùng `Task.WhenAll` để fetch parallel mỗi `RecordType`, dẫn đến `InvalidOperationException: A second operation was started on this context instance` — vì `DbContext` không thread-safe. Đã fix sang sequential `foreach await`. Khi viết `SduiPageConfig` mới mà cần fetch thêm data ngoài `RecordTypes`, **không tự gọi parallel** nếu dùng cùng repository injected (cùng DbContext). Pattern an toàn: dùng `IServiceScopeFactory` tạo scope riêng cho mỗi task, hoặc giữ sequential.

### 16.1 Pattern (ví dụ minh hoạ — chưa implement)

```csharp
// src/Services/DataMatchingService/DataMatchingService.Application/Sdui/Pages/BedOccupancySduiConfig.cs
public sealed class BedOccupancySduiConfig : SduiPageConfig
{
    public override string Code => "bed-occupancy";
    public override IReadOnlyList<string> RecordTypes => ["bed-occupancy"];

    public override SduiPage BuildPage(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate)
    {
        var rows = data.GetValueOrDefault("bed-occupancy", []);

        // ⚠️ LUÔN guard data rỗng — nếu không sẽ throw → 500
        if (rows.Count == 0)
            return new SduiPage(
                Code, "Bed Occupancy", "Trống", false,
                $"Chưa có dữ liệu cho ngày {reportDate:dd/MM/yyyy}",
                [], [], DateTime.UtcNow);

        // ... aggregate + build SduiPage ...
    }
}
```

Đăng ký DI (1 dòng trong `DependencyInjection.cs`):
```csharp
services.AddSingleton<SduiPageConfig, BedOccupancySduiConfig>();
```

Sau khi BE rebuild + restart:
```bash
curl http://localhost:5000/dm/pages
# ["bed-occupancy", "executive"]

curl http://localhost:5000/dm/pages/bed-occupancy
# trả layout chart cho data bed_occupancy
```

FE **không cần đổi gì** — `<SduiPageView>` đã generic.

### 16.2 Khi cần dropdown chọn page

```tsx
// src/components/sdui/PageSelector.tsx
'use client';
import { useEffect, useState } from 'react';
import { listDmPages } from '@/lib/dmClient';

export function PageSelector({ value, onChange }: { value: string; onChange: (c: string) => void }) {
  const [codes, setCodes] = useState<string[]>([]);
  useEffect(() => { listDmPages().then(setCodes); }, []);
  return (
    <select value={value} onChange={e => onChange(e.target.value)} className="border rounded px-2 py-1">
      {codes.map(c => <option key={c} value={c}>{c}</option>)}
    </select>
  );
}
```

---

## 17. Related docs (đọc tiếp)

| Doc | Khi đọc |
|---|---|
| [25 — SDUI](./25-sdui-server-driven-ui.md) | Hiểu trade-off SDUI vs Dashboard Engine |
| [38 — FE SDUI Guide (DynamicForm)](./38-frontend-sdui-implementation-guide.md) | Render screen từ `/forms/screens/...` — pattern tương tự nhưng cho form |
| [44 — Unified Ingest Pipeline](./44-unified-ingest-pipeline.md) | Tại sao data đến được `/dm/pages` |
| [45 — Auto-Enroll SourceProfile](./45-lakehouse-auto-sourceprofile.md) | MVP B `with-auto-profile` end-to-end |
| [46 — Playbook Add Source](./46-playbook-add-source-data.md) | Onboarding nguồn data mới step-by-step |
| [47 — Test MVP B Lakehouse View](./47-test-mvp-b-lakehouse-view.md) | QA verify pipeline trước khi consume FE |

---

## 18. Changelog

- **2026-06-08** — Initial. Cover endpoint `/dm/pages/{code}`, 5 component types, TS types, Next.js + Recharts renderer.
- **2026-06-08 (hotfix)** — Document pitfall: `SduiEngine` + `DashboardEngine` đã sửa từ `Task.WhenAll` → sequential `foreach await` để tránh `InvalidOperationException` khi 2+ record types fetch song song trên cùng `DbContext`. Thêm guard empty-data trong template `BuildPage` ở §16.
