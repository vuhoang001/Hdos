# 38 — Frontend SDUI Implementation Guide

> Tài liệu này hướng dẫn FE developer **implement đầy đủ** phần render Screen Layout
> trả về từ `/forms/screens/{moduleCode}/{screenCode}/layout`. Sau khi đọc + làm theo,
> bạn sẽ có dashboard hiển thị đúng KpiCard có giá trị thật, PieChart vẽ slice đúng,
> Table có row dữ liệu — không còn placeholder shell.
>
> Stack giả định: **Next.js 14 + React 18 + TypeScript + TailwindCSS + Recharts**.
> Có thể adapt sang Vue/Svelte vì logic chung.
>
> Tài liệu đi kèm: [`docs/37-huong-dan-toan-luong-cho-fresher.md`](./37-huong-dan-toan-luong-cho-fresher.md)
> (luồng BE đầy đủ).

---

## Mục lục

1. [Vấn đề hiện tại — Tại sao widget chỉ ra title?](#1-vấn-đề-hiện-tại)
2. [Kiến trúc FE — Component tree](#2-kiến-trúc-fe)
3. [Step-by-step: 5 việc FE phải làm](#3-step-by-step)
4. [Full code TypeScript — copy chạy được](#4-full-code)
5. [Service ID → Base URL resolver](#5-service-id-resolver)
6. [Authentication](#6-authentication)
7. [React Grid Layout integration](#7-grid-layout)
8. [Pitfalls đặc thù](#8-pitfalls)
9. [Test checklist](#9-test-checklist)

---

## 1. Vấn đề hiện tại

Sau khi BE setup xong dashboard (xem [docs/37 §7](./37-huong-dan-toan-luong-cho-fresher.md#7-widget-và-dashboard)),
FE mở screen thấy:

```
┌─────────────────────────────┐  ┌────────────────────┐
│ KpiCard                     │  │                    │
│ Tổng số bệnh nhân           │  │   (trống)          │
└─────────────────────────────┘  └────────────────────┘

┌──────────────────────────────────────────────────────┐
│ Table                                                │
│ Danh sách bệnh nhân                                  │
└──────────────────────────────────────────────────────┘
```

Đáng lẽ phải hiện:

```
┌─────────────────────────────┐  ┌────────────────────────────────┐
│ Tổng số bệnh nhân           │  │ Bệnh nhân theo Khoa            │
│                             │  │   🔵 Tim Mạch    5  (50%)       │
│        10 người             │  │   🟣 ICU         3  (30%)       │
│                             │  │   🟢 Nhi         2  (20%)       │
└─────────────────────────────┘  └────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│ Danh sách bệnh nhân                                              │
│ Họ tên          │ Khoa         │ Giường │ Chẩn đoán              │
│ Trần Bé 2       │ Khoa Nhi     │ NHI-02 │ Sốt cao co giật        │
│ Phạm Quỳnh Như  │ Tim Mạch     │ TM-12  │ Rối loạn nhịp tim      │
│ ...                                                              │
└──────────────────────────────────────────────────────────────────┘
```

**Lý do FE chưa render:** FE mới đọc `widgetType` + `config.title` để show shell,
nhưng **chưa làm 4 việc quan trọng**:

1. ❌ Chưa fetch các DataSources (URL trong `layout.dataSources[].resourcePath`)
2. ❌ Chưa evaluate expression `{{sources.x.y.z}}` thành giá trị thật
3. ❌ Chưa có widget renderer riêng cho từng `widgetType`
4. ❌ Chưa handle `canonicalAtKey` cho Table widget (cần JSON.parse field nested)

---

## 2. Kiến trúc FE

### 2.1 Component tree mong muốn

```
<App>
  └─ <ScreenPage moduleCode screenCode>           ← Next.js route /client?...
       └─ <ScreenRenderer moduleCode screenCode>
            │  Hook: useScreenLayout(moduleCode, screenCode) → layout
            │  Hook: useDataSources(layout.dataSources, urlParams) → sources
            │
            └─ <TabsContainer>
                 └─ <TabContent>
                      └─ <GridLayout>             ← react-grid-layout
                           └─ <WidgetRenderer widget sources>
                                ├─ if FormSection  → <FormSectionWidget>
                                ├─ if KpiCard      → <KpiCardWidget>
                                ├─ if PieChart     → <PieChartWidget>
                                ├─ if BarChart     → <BarChartWidget>
                                ├─ if Table        → <TableWidget>
                                └─ default         → <UnknownWidget>
```

### 2.2 Data flow

```
1. ScreenPage mounted
       ↓
2. useScreenLayout("hospital-dash", "overview")
   → GET /forms/screens/hospital-dash/overview/layout
   → layout = { dataSources, tabs, ... }
       ↓
3. useDataSources(layout.dataSources, urlParams)
   For each ds in dataSources:
     - extract params from urlParams (match ds.requiredParams)
     - interpolate ds.resourcePath with params
     - resolve base URL from ds.serviceId
     - fetch + Promise.all
   → sources = { record: {...}, patients: [...], kpi_khoa: {...} }
       ↓
4. Each widget evaluate expression với sources
       ↓
5. UI render
```

---

## 3. Step-by-step

### 3.1 Việc 1: Hook fetch layout

```typescript
// hooks/useScreenLayout.ts
import { useEffect, useState } from 'react';
import type { ScreenLayoutDto } from '@/types/sdui';

const BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL!;  // "https://192.168.100.60:8443"

export function useScreenLayout(moduleCode: string, screenCode: string) {
  const [layout, setLayout] = useState<ScreenLayoutDto | null>(null);
  const [error, setError]   = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const ctrl = new AbortController();
    (async () => {
      try {
        const token = localStorage.getItem('jwt');
        const res = await fetch(
          `${BASE_URL}/forms/screens/${moduleCode}/${screenCode}/layout`,
          {
            signal: ctrl.signal,
            headers: token ? { Authorization: `Bearer ${token}` } : {},
          }
        );
        if (!res.ok) throw new Error(`Layout HTTP ${res.status}`);
        const body = await res.json();
        if (!body.success) throw new Error(body.errorMessage || 'Layout fail');
        setLayout(body.data);
      } catch (e: any) {
        if (e.name !== 'AbortError') setError(e.message);
      } finally {
        setIsLoading(false);
      }
    })();
    return () => ctrl.abort();
  }, [moduleCode, screenCode]);

  return { layout, error, isLoading };
}
```

### 3.2 Việc 2: Service ID → Base URL resolver

```typescript
// lib/services.ts
const SERVICE_BASE_URLS: Record<string, string> = {
  datamatch:   process.env.NEXT_PUBLIC_API_BASE_URL!,   // tất cả qua gateway
  dynform:     process.env.NEXT_PUBLIC_API_BASE_URL!,
  m01:         process.env.NEXT_PUBLIC_API_BASE_URL!,
  auth:        process.env.NEXT_PUBLIC_API_BASE_URL!,
  // ... thêm khi có service mới
};

export function getServiceBaseUrl(serviceId: string): string {
  const url = SERVICE_BASE_URLS[serviceId];
  if (!url) throw new Error(`Unknown serviceId: "${serviceId}"`);
  return url;
}
```

### 3.3 Việc 3: Hook fetch DataSources song song

```typescript
// hooks/useDataSources.ts
import { useEffect, useState } from 'react';
import type { DataSourceDto } from '@/types/sdui';
import { getServiceBaseUrl } from '@/lib/services';

export type Sources = Record<string, unknown>;

function interpolatePath(template: string, params: Record<string, string>): string {
  return template.replace(/\{(\w+)\}/g, (_, key) => {
    if (params[key] === undefined) throw new Error(`Missing param: ${key}`);
    return encodeURIComponent(params[key]);
  });
}

function extractParams(
  required: string[],
  urlParams: URLSearchParams
): Record<string, string> {
  const out: Record<string, string> = {};
  for (const p of required) {
    const v = urlParams.get(p);
    if (v === null) throw new Error(`URL thiếu param: ${p}`);
    out[p] = v;
  }
  return out;
}

export function useDataSources(
  dataSources: DataSourceDto[] | undefined,
  urlParams: URLSearchParams
) {
  const [sources, setSources]     = useState<Sources>({});
  const [error, setError]         = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    if (!dataSources || dataSources.length === 0) {
      setIsLoading(false);
      return;
    }

    const ctrl = new AbortController();
    (async () => {
      try {
        const token = localStorage.getItem('jwt');
        const results = await Promise.all(
          dataSources.map(async (ds) => {
            const params = extractParams(ds.requiredParams, urlParams);
            const path   = interpolatePath(ds.resourcePath, params);
            const url    = getServiceBaseUrl(ds.serviceId) + path;

            const res = await fetch(url, {
              signal: ctrl.signal,
              headers: token ? { Authorization: `Bearer ${token}` } : {},
            });
            if (!res.ok) throw new Error(`${ds.namespace}: HTTP ${res.status}`);
            const body = await res.json();
            // Tất cả endpoint trả về { success, data, errorCode, errorMessage }
            return [ds.namespace, body.data] as const;
          })
        );
        setSources(Object.fromEntries(results));
      } catch (e: any) {
        if (e.name !== 'AbortError') setError(e.message);
      } finally {
        setIsLoading(false);
      }
    })();
    return () => ctrl.abort();
  }, [dataSources, urlParams]);

  return { sources, error, isLoading };
}
```

### 3.4 Việc 4: Mustache evaluator + format

```typescript
// lib/expression.ts

export function resolvePath(obj: unknown, path: string): unknown {
  let cur: any = obj;
  for (const key of path.split('.')) {
    if (cur == null) return undefined;
    cur = cur[key];
  }
  return cur;
}

/**
 * Evaluate "{{sources.record.HoTen}}" → giá trị tương ứng.
 * Hỗ trợ 2 mode:
 *   - Full expression: "{{sources.x.y}}" → trả về raw value (có thể là object/array/string)
 *   - Embedded: "Xin chào {{sources.record.HoTen}}" → replace inline thành string
 */
export function evaluate(
  expression: string,
  sources: Record<string, unknown>
): unknown {
  if (typeof expression !== 'string') return expression;

  // Full expression mode: chỉ có 1 cặp {{}} chiếm trọn chuỗi
  const fullMatch = expression.match(/^\{\{\s*sources\.([\w.]+)\s*\}\}$/);
  if (fullMatch) {
    return resolvePath({ sources }, `sources.${fullMatch[1]}`);
  }

  // Embedded mode: replace inline
  return expression.replace(/\{\{\s*sources\.([\w.]+)\s*\}\}/g, (_, path) => {
    const v = resolvePath({ sources }, `sources.${path}`);
    return v == null ? '' : String(v);
  });
}

/** Format value theo displayFormat hint */
export function formatValue(val: unknown, format?: string | null): string {
  if (val == null) return '';
  const s = String(val);
  if (!format) return s;

  if (format.startsWith('date:')) {
    const pattern = format.slice(5);
    const d = new Date(s);
    if (isNaN(d.getTime())) return s;
    return pattern
      .replace('DD',   String(d.getDate()).padStart(2, '0'))
      .replace('MM',   String(d.getMonth() + 1).padStart(2, '0'))
      .replace('YYYY', String(d.getFullYear()));
  }

  if (format === 'currency:VND') {
    const n = Number(val);
    return isNaN(n) ? s : n.toLocaleString('vi-VN') + ' ₫';
  }

  if (format === 'percent') {
    const n = Number(val);
    return isNaN(n) ? s : (n * 100).toFixed(1) + '%';
  }

  return s;
}
```

### 3.5 Việc 5: Widget renderer + dispatcher

```typescript
// components/widgets/WidgetRenderer.tsx
import type { ScreenLayoutWidgetDto } from '@/types/sdui';
import type { Sources } from '@/hooks/useDataSources';
import { KpiCardWidget }     from './KpiCardWidget';
import { PieChartWidget }    from './PieChartWidget';
import { BarChartWidget }    from './BarChartWidget';
import { TableWidget }       from './TableWidget';
import { FormSectionWidget } from './FormSectionWidget';

export function WidgetRenderer({
  widget, sources,
}: {
  widget: ScreenLayoutWidgetDto;
  sources: Sources;
}) {
  const cfg = (widget.config ?? {}) as Record<string, any>;

  switch (widget.widgetType) {
    case 'FormSection':
      return <FormSectionWidget formSchema={widget.formSchema!} sources={sources} />;
    case 'KpiCard':
      return <KpiCardWidget config={cfg} sources={sources} />;
    case 'PieChart':
      return <PieChartWidget config={cfg} sources={sources} />;
    case 'BarChart':
      return <BarChartWidget config={cfg} sources={sources} />;
    case 'Table':
      return <TableWidget config={cfg} sources={sources} />;
    case 'TextBlock':
      return <div className="prose">{cfg.text ?? ''}</div>;
    case 'Divider':
      return <hr className="my-4 border-gray-200" />;
    default:
      return (
        <div className="p-4 bg-yellow-50 border border-yellow-200 rounded">
          ⚠️ Widget chưa hỗ trợ: <code>{widget.widgetType}</code>
        </div>
      );
  }
}
```

---

## 4. Full code

### 4.1 Types (TypeScript shape khớp DTO của BE)

```typescript
// types/sdui.ts

export interface ScreenLayoutDto {
  id:           string;
  moduleCode:   string;
  code:         string;
  title:        string;
  description?: string | null;
  dataSources:  DataSourceDto[];
  tabs:         ScreenLayoutTabDto[];
  generatedAt:  string;
}

export interface DataSourceDto {
  namespace:      string;          // "record"
  serviceId:      string;          // "datamatch"
  resourcePath:   string;          // "/dm/records/{recordId}"
  requiredParams: string[];        // ["recordId"]
}

export interface ScreenLayoutTabDto {
  id:        string;
  label:     string;
  slug:      string;
  sortOrder: number;
  isDefault: boolean;
  widgets:   ScreenLayoutWidgetDto[];
}

export interface ScreenLayoutWidgetDto {
  widgetKey:   string;
  widgetType:  string;             // "KpiCard" | "PieChart" | "Table" | "FormSection"...
  gridX:       number;
  gridY:       number;
  gridW:       number;
  gridH:       number;
  config:      Record<string, any> | null;
  referenceId: string | null;
  formSchema:  FormSchemaDto | null;
}

export interface FormSchemaDto {
  id:          string;
  moduleCode:  string;
  formKey:     string;
  name:        string;
  description?: string | null;
  version:     number;
  fields:      FormFieldDto[];
  settings:    FormSettingsDto;
}

export interface FormFieldDto {
  id:                string;
  key:               string;
  label:             string;
  type:              string;       // "Text" | "Date" | "Select"...
  order:             number;
  required:          boolean;
  width:             string;       // "Full" | "Half" | "Third"
  placeholder?:      string | null;
  helpText?:         string | null;
  options?:          FieldOptionDto[]    | null;
  validationRules?:  ValidationRuleDto[] | null;
  conditionalLogic?: ConditionalLogicDto | null;
  dataBinding?:      DataBindingDto      | null;
  isReadOnly:        boolean;
}

export interface DataBindingDto {
  expression:    string;           // "{{sources.record.HoTen}}"
  displayFormat: string | null;    // "date:DD/MM/YYYY"
}

export interface FormSettingsDto {
  submitButtonLabel:        string;
  successMessage:           string;
  allowMultipleSubmissions: boolean;
}

export interface FieldOptionDto    { label: string; value: string; }
export interface ValidationRuleDto { type: string; value: string; errorMessage: string; }
export interface ConditionalLogicDto {
  sourceFieldKey: string;
  operator:       string;
  value:          string;
  action:         string;
}
```

### 4.2 ScreenRenderer (top-level)

```tsx
// components/ScreenRenderer.tsx
'use client';
import { useSearchParams } from 'next/navigation';
import { useState }        from 'react';
import { useScreenLayout } from '@/hooks/useScreenLayout';
import { useDataSources }  from '@/hooks/useDataSources';
import { WidgetRenderer }  from './widgets/WidgetRenderer';

export function ScreenRenderer({
  moduleCode, screenCode,
}: { moduleCode: string; screenCode: string }) {
  const urlParams = useSearchParams();
  const { layout, isLoading: layoutLoading, error: layoutError } =
    useScreenLayout(moduleCode, screenCode);

  const { sources, isLoading: dataLoading, error: dataError } =
    useDataSources(layout?.dataSources, urlParams);

  const [activeTab, setActiveTab] = useState<string | undefined>();

  if (layoutLoading) return <Skeleton label="Đang tải layout..." />;
  if (layoutError)   return <ErrorBox msg={layoutError} />;
  if (!layout)       return <ErrorBox msg="Không tìm thấy screen" />;

  const tab = layout.tabs.find(t => t.slug === activeTab)
           ?? layout.tabs.find(t => t.isDefault)
           ?? layout.tabs[0];

  return (
    <div className="space-y-4">
      <header>
        <h1 className="text-2xl font-bold">{layout.title}</h1>
        {layout.description && (
          <p className="text-sm text-gray-500">{layout.description}</p>
        )}
      </header>

      {layout.tabs.length > 1 && (
        <nav className="flex gap-2 border-b">
          {layout.tabs.map(t => (
            <button
              key={t.id}
              onClick={() => setActiveTab(t.slug)}
              className={`px-3 py-2 ${t === tab ? 'border-b-2 border-blue-500' : ''}`}
            >
              {t.label}
            </button>
          ))}
        </nav>
      )}

      {dataLoading && <Skeleton label="Đang tải dữ liệu..." />}
      {dataError   && <ErrorBox msg={dataError} />}

      {!dataLoading && tab && (
        <GridLayout cols={24}>
          {tab.widgets.map(w => (
            <GridItem key={w.widgetKey}
                      x={w.gridX} y={w.gridY} w={w.gridW} h={w.gridH}>
              <div className="h-full rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
                <WidgetRenderer widget={w} sources={sources} />
              </div>
            </GridItem>
          ))}
        </GridLayout>
      )}
    </div>
  );
}

function Skeleton({ label }: { label: string }) {
  return <div className="animate-pulse text-gray-500">{label}</div>;
}
function ErrorBox({ msg }: { msg: string }) {
  return <div className="text-red-600 p-4 bg-red-50 rounded">⚠️ {msg}</div>;
}
```

### 4.3 KpiCardWidget

```tsx
// components/widgets/KpiCardWidget.tsx
import { evaluate, formatValue } from '@/lib/expression';
import type { Sources } from '@/hooks/useDataSources';

interface KpiCardConfig {
  title?:           string;
  valueExpression?: string;
  unit?:            string;
  color?:           string;          // hex
  displayFormat?:   string | null;
  icon?:            string;
}

export function KpiCardWidget({
  config, sources,
}: { config: KpiCardConfig; sources: Sources }) {
  const raw = evaluate(config.valueExpression ?? '', sources);
  const val = formatValue(raw, config.displayFormat);

  return (
    <div className="h-full flex flex-col">
      <div className="text-xs uppercase text-gray-500 tracking-wide">
        {config.title}
      </div>
      <div className="mt-2 flex-1 flex items-center">
        <div className="text-4xl font-bold" style={{ color: config.color ?? '#111' }}>
          {val || '—'}
        </div>
        {config.unit && (
          <div className="ml-2 text-sm text-gray-400">{config.unit}</div>
        )}
      </div>
    </div>
  );
}
```

### 4.4 PieChartWidget (với Recharts)

```tsx
// components/widgets/PieChartWidget.tsx
import { PieChart, Pie, Cell, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import { evaluate } from '@/lib/expression';
import type { Sources } from '@/hooks/useDataSources';

interface PieChartConfig {
  title?:          string;
  chartType?:      string;
  dataExpression?: string;
  rowPath?:        string;            // nếu mỗi row có nested ("data")
  labelField?:     string;
  valueField?:     string;
  colors?:         string[];
}

export function PieChartWidget({
  config, sources,
}: { config: PieChartConfig; sources: Sources }) {
  const rows = (evaluate(config.dataExpression ?? '', sources) ?? []) as any[];

  // Unpack nested .data nếu config.rowPath set
  const data = rows.map(r => {
    const item = config.rowPath ? (r[config.rowPath] ?? r) : r;
    return {
      name:  item[config.labelField  ?? 'label'],
      value: Number(item[config.valueField ?? 'value']) || 0,
    };
  });

  const colors = config.colors ?? ['#6366f1', '#ec4899', '#10b981', '#f59e0b', '#06b6d4'];

  return (
    <div className="h-full flex flex-col">
      {config.title && (
        <div className="text-sm font-semibold mb-2">{config.title}</div>
      )}
      <div className="flex-1 min-h-0">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie data={data} dataKey="value" nameKey="name"
                 cx="50%" cy="50%" outerRadius="80%" label>
              {data.map((_, i) => (
                <Cell key={i} fill={colors[i % colors.length]} />
              ))}
            </Pie>
            <Tooltip />
            <Legend />
          </PieChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}
```

### 4.5 BarChartWidget

```tsx
// components/widgets/BarChartWidget.tsx
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer } from 'recharts';
import { evaluate } from '@/lib/expression';
import type { Sources } from '@/hooks/useDataSources';

interface BarChartConfig {
  title?:          string;
  dataExpression?: string;
  rowPath?:        string;
  labelField?:     string;
  valueField?:     string;
  color?:          string;
}

export function BarChartWidget({
  config, sources,
}: { config: BarChartConfig; sources: Sources }) {
  const rows = (evaluate(config.dataExpression ?? '', sources) ?? []) as any[];
  const data = rows.map(r => {
    const item = config.rowPath ? (r[config.rowPath] ?? r) : r;
    return {
      name:  item[config.labelField  ?? 'label'],
      value: Number(item[config.valueField ?? 'value']) || 0,
    };
  });

  return (
    <div className="h-full flex flex-col">
      {config.title && <div className="text-sm font-semibold mb-2">{config.title}</div>}
      <div className="flex-1 min-h-0">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={data}>
            <XAxis dataKey="name" />
            <YAxis />
            <Tooltip />
            <Bar dataKey="value" fill={config.color ?? '#6366f1'} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}
```

### 4.6 TableWidget (chú ý canonicalAtKey)

```tsx
// components/widgets/TableWidget.tsx
import { evaluate, formatValue } from '@/lib/expression';
import type { Sources } from '@/hooks/useDataSources';

interface TableColumn {
  field:         string;
  header:        string;
  width?:        number;
  displayFormat?: string | null;
}

interface TableConfig {
  title?:          string;
  dataExpression?: string;
  /** Nếu data từ /dm/records → mỗi row có field canonicalPayload là JSON string */
  canonicalAtKey?: string;
  columns:         TableColumn[];
  emptyMessage?:   string;
}

export function TableWidget({
  config, sources,
}: { config: TableConfig; sources: Sources }) {
  const rawRows = (evaluate(config.dataExpression ?? '', sources) ?? []) as any[];

  // Unpack canonicalAtKey: nếu có, parse JSON string thành object
  const rows = rawRows.map(r => {
    if (config.canonicalAtKey && typeof r[config.canonicalAtKey] === 'string') {
      try { return { ...r, ...JSON.parse(r[config.canonicalAtKey]) }; }
      catch { return r; }
    }
    return r;
  });

  return (
    <div className="h-full flex flex-col">
      {config.title && (
        <div className="text-sm font-semibold mb-2">{config.title}</div>
      )}
      <div className="flex-1 min-h-0 overflow-auto">
        <table className="w-full text-sm">
          <thead className="bg-gray-50 sticky top-0">
            <tr>
              {config.columns.map(c => (
                <th key={c.field}
                    className="text-left px-3 py-2 font-medium text-gray-700"
                    style={{ width: c.width }}>
                  {c.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 && (
              <tr>
                <td colSpan={config.columns.length}
                    className="text-center py-8 text-gray-400">
                  {config.emptyMessage ?? 'Không có dữ liệu'}
                </td>
              </tr>
            )}
            {rows.map((r, i) => (
              <tr key={i} className="border-t hover:bg-gray-50">
                {config.columns.map(c => (
                  <td key={c.field} className="px-3 py-2">
                    {formatValue(r[c.field], c.displayFormat)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
```

### 4.7 FormSectionWidget (form input với binding)

```tsx
// components/widgets/FormSectionWidget.tsx
'use client';
import { useState, useMemo } from 'react';
import { evaluate, formatValue } from '@/lib/expression';
import type { FormSchemaDto, FormFieldDto } from '@/types/sdui';
import type { Sources } from '@/hooks/useDataSources';

const BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL!;

export function FormSectionWidget({
  formSchema, sources,
}: { formSchema: FormSchemaDto; sources: Sources }) {
  // Resolve bound fields → initial values
  const initial = useMemo(() => {
    const o: Record<string, string> = {};
    for (const f of formSchema.fields) {
      if (f.dataBinding) {
        const raw = evaluate(f.dataBinding.expression, sources);
        o[f.key] = formatValue(raw, f.dataBinding.displayFormat);
      } else {
        o[f.key] = '';
      }
    }
    return o;
  }, [formSchema, sources]);

  const [values, setValues] = useState(initial);
  const [submitting, setSubmitting] = useState(false);
  const [success, setSuccess] = useState<string | null>(null);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    try {
      // Chỉ submit field user-editable
      const answers = formSchema.fields
        .filter(f => !f.isReadOnly)
        .map(f => ({ fieldKey: f.key, value: values[f.key] ?? '' }));

      const token = localStorage.getItem('jwt');
      const res = await fetch(
        `${BASE_URL}/forms/${formSchema.moduleCode}/${formSchema.formKey}/submit`,
        {
          method:  'POST',
          headers: {
            'Content-Type':  'application/json',
            ...(token ? { Authorization: `Bearer ${token}` } : {}),
          },
          body: JSON.stringify({ answers }),
        }
      );
      const body = await res.json();
      if (!body.success) throw new Error(body.errorMessage);
      setSuccess(formSchema.settings.successMessage);
    } catch (err: any) {
      alert(err.message);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form onSubmit={onSubmit} className="space-y-3">
      {formSchema.fields
        .sort((a, b) => a.order - b.order)
        .map(f => (
          <FieldInput key={f.id} field={f} value={values[f.key] ?? ''}
                      onChange={v => setValues(s => ({ ...s, [f.key]: v }))} />
        ))}

      {success ? (
        <div className="text-green-600 text-sm">✓ {success}</div>
      ) : (
        <button type="submit" disabled={submitting}
                className="px-4 py-2 bg-blue-600 text-white rounded disabled:opacity-50">
          {submitting ? 'Đang gửi...' : formSchema.settings.submitButtonLabel}
        </button>
      )}
    </form>
  );
}

function FieldInput({
  field, value, onChange,
}: {
  field:    FormFieldDto;
  value:    string;
  onChange: (v: string) => void;
}) {
  const common = {
    value,
    onChange: (e: any) => onChange(e.target.value),
    disabled: field.isReadOnly,
    placeholder: field.placeholder ?? '',
    required: field.required,
    className: 'w-full border rounded px-3 py-2 disabled:bg-gray-50 disabled:text-gray-600',
  };

  let input: React.ReactNode;
  switch (field.type) {
    case 'Textarea':
      input = <textarea rows={3} {...common} />; break;
    case 'Date':
      input = <input type="date" {...common} />; break;
    case 'DateTime':
      input = <input type="datetime-local" {...common} />; break;
    case 'Number':
      input = <input type="number" {...common} />; break;
    case 'Select':
      input = (
        <select {...common}>
          <option value="">— Chọn —</option>
          {field.options?.map(o => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </select>
      ); break;
    default:
      input = <input type="text" {...common} />;
  }

  return (
    <div>
      <label className="block text-sm font-medium mb-1">
        {field.label}{field.required && <span className="text-red-500">*</span>}
      </label>
      {input}
      {field.helpText && (
        <p className="text-xs text-gray-500 mt-1">{field.helpText}</p>
      )}
    </div>
  );
}
```

### 4.8 Next.js page

```tsx
// app/client/page.tsx
import { ScreenRenderer } from '@/components/ScreenRenderer';

export default function ClientPage({
  searchParams,
}: {
  searchParams: { module?: string; screen?: string };
}) {
  const { module, screen } = searchParams;
  if (!module || !screen) {
    return <div>Thiếu query param ?module=&screen=</div>;
  }
  return (
    <div className="container mx-auto p-4">
      <ScreenRenderer moduleCode={module} screenCode={screen} />
    </div>
  );
}
```

---

## 5. Service ID resolver

Trong [§3.2](#32-việc-2-service-id--base-url-resolver) đã có. Các điểm cần nhớ:

- **Mọi service hiện đều qua nginx gateway** → cùng base URL
- Tương lai có thể tách subdomain: `datamatch.hdos.local`, `dynform.hdos.local` → chỉ cần update map `SERVICE_BASE_URLS`
- KHÔNG hardcode URL trong widget config — luôn dùng `serviceId` để FE tự resolve

---

## 6. Authentication

### 6.1 Lưu JWT sau login

```typescript
// app/login/page.tsx (sau khi login thành công)
const body = await fetch('/auth/login', {...}).then(r => r.json());
localStorage.setItem('jwt', body.data.token);
localStorage.setItem('userId', body.data.userId);
router.push('/client?module=...&screen=...');
```

### 6.2 Tự động refresh khi 401

Trong `useDataSources` / `useScreenLayout`, nếu fetch trả 401:
```typescript
if (res.status === 401) {
  localStorage.removeItem('jwt');
  window.location.href = '/login';
  return;
}
```

### 6.3 Endpoint chưa bật `[Authorize]`

Hiện một số endpoint admin (như `/forms/admin/generate-from-source`) đã comment out
`[Authorize]` cho dễ demo. Production phải bật lại — FE phải gửi JWT cho cả admin
endpoint, không chỉ user endpoint.

---

## 7. Grid Layout

Hdos screen dùng grid 24 cột. Widget có `gridX`, `gridY`, `gridW`, `gridH`. Có 2 lựa chọn:

### 7.1 Simple CSS Grid (read-only render)

```tsx
function GridLayout({ children, cols }: { children: React.ReactNode; cols: number }) {
  return (
    <div className="grid gap-4" style={{ gridTemplateColumns: `repeat(${cols}, 1fr)` }}>
      {children}
    </div>
  );
}

function GridItem({
  x, y, w, h, children,
}: { x: number; y: number; w: number; h: number; children: React.ReactNode }) {
  return (
    <div style={{
      gridColumn: `${x + 1} / span ${w}`,
      gridRow:    `${y + 1} / span ${h}`,
      minHeight:  h * 40,    // 40px / unit
    }}>
      {children}
    </div>
  );
}
```

### 7.2 react-grid-layout (cho admin designer drag-drop)

Khi cần admin kéo thả → dùng [`react-grid-layout`](https://github.com/react-grid-layout/react-grid-layout):

```bash
npm install react-grid-layout
```

```tsx
import GridLayout from 'react-grid-layout';
import 'react-grid-layout/css/styles.css';

<GridLayout cols={24} rowHeight={40} width={1200}
            layout={widgets.map(w => ({
              i: w.widgetKey, x: w.gridX, y: w.gridY, w: w.gridW, h: w.gridH,
            }))}
            onLayoutChange={newLayout => {
              // map newLayout → PUT /forms/admin/screens/.../widgets
            }}>
  {widgets.map(w => (
    <div key={w.widgetKey}><WidgetRenderer widget={w} sources={sources} /></div>
  ))}
</GridLayout>
```

---

## 8. Pitfalls

### 8.1 `config` có thể là object hoặc string

Tùy chuyện BE serialize JSONB → object hay giữ nguyên string. Code phòng thủ:

```typescript
const cfg: any = typeof widget.config === 'string'
  ? JSON.parse(widget.config)
  : (widget.config ?? {});
```

Hiện BE Hdos trả về object (đã parse) — nhưng phòng khi version cũ trả string.

### 8.2 `canonicalAtKey` — chỉ áp dụng khi DataSource là `/dm/records?...`

Endpoint `/dm/records?...` trả về `StagingRecordDto[]` mỗi item có:
```json
{ "id": "...", "canonicalPayload": "{\"HoTen\":\"...\"}", "..." }
```

`canonicalPayload` là JSON **string** → phải parse trước khi access `HoTen`. FE handle bằng `canonicalAtKey: "canonicalPayload"` (xem [§4.6](#46-tablewidget-chú-ý-canonicalatkey)).

Nếu DataSource là `/dm/reports/...` → data đã flat, KHÔNG cần canonicalAtKey.

### 8.3 `rowPath` cho Chart

Endpoint reports trả về:
```json
{ "rows": [ { "data": { "TenKhoa": "...", "SoBenhNhan": 5 } }, ... ] }
```

Mỗi row nested 1 level qua `.data`. PieChart/BarChart phải có `rowPath: "data"` để unpack:

```typescript
const item = config.rowPath ? row[config.rowPath] : row;
```

Reports nào trả flat thì bỏ `rowPath`.

### 8.4 Empty array vs undefined

```typescript
// ❌ Sai — undefined không có .map
const rows = evaluate(cfg.dataExpression, sources);
rows.map(...);

// ✅ Đúng — fallback []
const rows = (evaluate(cfg.dataExpression, sources) ?? []) as any[];
rows.map(...);
```

### 8.5 Expression có space hoặc viết hoa

| Expression | OK? |
|---|---|
| `{{sources.record.HoTen}}` | ✓ |
| `{{ sources.record.HoTen }}` | ✓ (regex tolerant với `\s*`) |
| `{{Sources.record.HoTen}}` | ✗ — `Sources` viết hoa |
| `{{sources.record.hoten}}` | ✗ — JSON case-sensitive |

JSON key luôn case-sensitive. Khi đăng ký SourceProfile mappings → giá trị canonical (vd `HoTen`) phải match đúng với expression.

### 8.6 React Strict Mode double-fetch

Trong dev mode React StrictMode mount component 2 lần → fetch 2 lần. Trong `useScreenLayout` đã có `AbortController` để hủy request cũ — OK.

Nhưng nếu thấy 2 record được ingest (do 1 user action) trong dev mode → vấn đề strict mode. Production OK.

### 8.7 SSE/realtime update

Hiện không có. Nếu cần dashboard auto-refresh:
- Option 1: `setInterval` re-fetch DataSources mỗi 30s
- Option 2: Subscribe SSE `/notifications/sse` để BE push event khi data thay đổi

---

## 9. Test checklist

### 9.1 Smoke test (manual)

```
□ Mở http://localhost:3000/client?module=hospital-dash-XXX&screen=overview
□ DevTools → Network thấy:
    GET /forms/screens/.../layout                       → 200
    GET /dm/records?sourceSystem=his-fresher&...        → 200
    GET /dm/reports/benh-nhan-theo-khoa?...             → 200
□ KpiCard hiện giá trị số (không phải "—")
□ PieChart vẽ slice
□ Table có row với họ tên/khoa/giường/chẩn đoán
□ Console không có error
```

### 9.2 Edge case test

```
□ Empty DataSource → widget hiện "Không có dữ liệu", không crash
□ Expression sai (vd "{{sources.xxx.yyy}}") → widget hiện "—", không crash
□ JWT hết hạn → redirect /login
□ Network slow → loading skeleton hiện
□ Resize window → grid responsive (nếu dùng react-grid-layout)
```

### 9.3 Unit test (Jest + RTL)

```typescript
// __tests__/expression.test.ts
import { evaluate, formatValue, resolvePath } from '@/lib/expression';

describe('evaluate', () => {
  const sources = {
    record:   { HoTen: 'An', address: { city: 'HN' } },
    patients: [{ HoTen: 'A' }, { HoTen: 'B' }],
  };

  test('full expression returns raw value', () => {
    expect(evaluate('{{sources.record.HoTen}}', sources)).toBe('An');
    expect(evaluate('{{sources.patients}}', sources)).toHaveLength(2);
  });

  test('nested path', () => {
    expect(evaluate('{{sources.record.address.city}}', sources)).toBe('HN');
  });

  test('embedded mode replaces inline', () => {
    expect(evaluate('Xin chào {{sources.record.HoTen}}', sources))
      .toBe('Xin chào An');
  });

  test('missing path returns undefined / empty', () => {
    expect(evaluate('{{sources.xxx}}', sources)).toBeUndefined();
    expect(evaluate('Hello {{sources.xxx}}', sources)).toBe('Hello ');
  });
});

describe('formatValue', () => {
  test('date format', () => {
    expect(formatValue('1992-08-14', 'date:DD/MM/YYYY')).toBe('14/08/1992');
  });
  test('currency', () => {
    expect(formatValue(150000, 'currency:VND')).toBe('150.000 ₫');
  });
});
```

### 9.4 Verify với script BE

```bash
# Tạo dataset mới rồi mở URL được in ra
bash scripts/demo-dashboard-flow.sh
# → Lấy URL "Layout SDUI" từ output, paste vào FE:
#   http://localhost:3000/client?module=hospital-dash-XXX&screen=overview
```

---

## 10. Roadmap mở rộng

Sau khi 4 widget cơ bản chạy, thêm dần:

| Tính năng | Mức độ phức tạp | Mô tả |
|---|---|---|
| ConditionalLogic Show/Hide | Thấp | Field hiển thị khi field khác = giá trị |
| ValidationRules client-side | Thấp | minLength/maxLength/pattern + error inline |
| react-grid-layout drag-drop | Trung bình | Admin kéo thả widget, save về BE |
| Multi-tab navigation | Thấp | Đã có khung trong ScreenRenderer |
| File upload field | Trung bình | Type "File" → upload tới MinIO/S3 |
| Signature field | Trung bình | `react-signature-canvas` |
| Realtime refresh | Trung bình | SSE hoặc polling |
| Theme/Dark mode | Thấp | CSS variables từ widget.config.color |
| i18n labels | Trung bình | Field label đa ngữ qua `labels: {vi, en}` |

---

## Tóm tắt

```
┌──────────────────────────────────────────────────────────────────────┐
│                                                                      │
│  3 thứ FE cần làm khi mở 1 screen:                                   │
│                                                                      │
│  1. FETCH LAYOUT                                                     │
│     GET /forms/screens/{m}/{s}/layout                                │
│     → { dataSources, tabs[].widgets[] }                              │
│                                                                      │
│  2. FETCH SOURCES (song song)                                        │
│     For each ds in dataSources:                                      │
│       url = baseUrl(ds.serviceId) + interpolate(ds.resourcePath)     │
│       sources[ds.namespace] = await fetch(url).json().data           │
│                                                                      │
│  3. RENDER với evaluate({{sources.x.y}}, sources)                    │
│     KpiCard.valueExpression  → text                                  │
│     PieChart.dataExpression  → array → recharts                      │
│     Table.dataExpression     → rows → table                          │
│     FormSection.formSchema   → form input với dataBinding            │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘

KEY CODE:
  hooks/useScreenLayout.ts        (~30 dòng)
  hooks/useDataSources.ts         (~50 dòng)
  lib/expression.ts               (~40 dòng) — evaluator + formatter
  lib/services.ts                 (~10 dòng) — serviceId → baseUrl
  components/ScreenRenderer.tsx   (~70 dòng) — top-level
  components/widgets/             (~50-80 dòng / widget)
    KpiCardWidget.tsx
    PieChartWidget.tsx
    BarChartWidget.tsx
    TableWidget.tsx
    FormSectionWidget.tsx

DEPENDENCIES:
  next react react-dom typescript          (core)
  recharts                                  (chart)
  react-grid-layout (optional)              (drag-drop)
  tailwindcss                               (styling)
```

Sau khi implement xong → reload trang dashboard → widget có data, không còn shell trống.
