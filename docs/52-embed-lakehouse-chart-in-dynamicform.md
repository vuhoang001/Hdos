# 52 — Embed Lakehouse Chart (Path B) vào DynamicForm Screen

> **Mục đích.** Hướng dẫn cách **nhúng** chart Path B (`/lakehouse/charts/{code}`)
> vào màn DynamicForm để admin dùng screen designer kéo-thả chart, FE render unified.
>
> **Mức độ:** Hiện tại đã set up **Mức 1** (loose coupling qua Provider/Operation).
> **Mức 2** (widget catalog `embed_sdui_page` + FE renderer) là next step — phần này
> ghi rõ BE đã expose gì, FE cần làm gì.
>
> **Tài liệu liên quan:**
> - [doc 41 — Loose Coupling Architecture (Provider Catalog)](./41-loose-coupling-architecture.md)
> - [doc 48 — FE Consume /dm/pages](./48-frontend-consume-dm-pages-chart-guide.md)
> - [doc 50 — BE Path B Chart](./50-add-new-lakehouse-chart-guide.md)
> - [doc 51 — System Overview](./51-charts-system-overview.md)

---

## 0. TL;DR

```
Cách FE nhúng chart Path B vào DynamicForm screen:

[1] Admin resolve URL từ Provider Catalog (đã setup):
    Provider:  lakehouse / baseUrl: http://lakehouseservice:8080
    Operation: chart-page / pattern: /lakehouse/charts/{code}
    → Full URL: http://lakehouseservice:8080/lakehouse/charts/{code}

[2] Admin thiết kế screen widget với type "embed_sdui_page" (chưa có
    trong WidgetCatalog — Mức 2 TODO).

[3] FE render screen: gặp widget kiểu này → fetch URL → render nested
    với <SduiPageView /> (doc 48).
```

---

## 1. Hiện trạng (2026-06-08)

### 1.1 Provider Catalog (đã set up)

```bash
GET https://192.168.100.60:8443/forms/admin/providers
→ {
    "code":           "lakehouse",
    "displayName":    "Lakehouse Charts (direct SQL)",
    "baseUrl":        "http://lakehouseservice:8080",
    "operationCount": 1
  }

GET https://192.168.100.60:8443/forms/admin/providers/lakehouse/operations
→ {
    "operationKey":   "chart-page",
    "combinedRef":    "lakehouse::chart-page",
    "pattern":        "/lakehouse/charts/{code}",
    "requiredParams": ["code"],
    "kind":           "Single"
  }
```

→ Admin tạo DataSource trong screen designer **đã thấy** `lakehouse::chart-page` trong dropdown.

### 1.2 WidgetCatalog (chưa có entry phù hợp)

```bash
GET https://192.168.100.60:8443/forms/admin/widget-catalog
→ [line_chart, bar_chart, pie_chart, kpi, gauge, ...]
```

**Vấn đề:** Các widget hiện có (`line_chart`, `pie_chart`, ...) expect data shape **flat** kiểu `{ data: [...], xAxis: [...] }`. Trong khi `/lakehouse/charts/{code}` trả **`SduiPage`** với `rows[].components[]` polymorphic.

→ **Cần thêm widget catalog entry mới** `embed_sdui_page` với schema phù hợp (Mức 2).

---

## 2. Kiến trúc tổng — 3 mức integration

### Mức 0 — FE-only (đơn giản nhất, không cần BE)

FE viết route riêng cho dashboard:
```
/forms/screens/<module>/<screen>     → render screen
/dashboards/<code>                    → fetch /lakehouse/charts/<code>, render
```

→ 2 route tách biệt, không nhúng vào nhau.

### Mức 1 — Loose coupling (đã setup ở §1.1)

Admin **biết URL** qua Provider Catalog, nhưng FE vẫn fetch + render thủ công ngoài screen designer.

→ Lợi: URL không hardcode trong FE. Đổi infra → sửa 1 dòng Operation, FE tự cập nhật.
→ Hạn chế: vẫn không "drag & drop" được trong designer.

### Mức 2 — Drag & drop widget (FE TODO)

```
[A] BE add WidgetCatalog entry "embed_sdui_page":
    POST /forms/admin/widget-catalog
    {
      "chartType":       "embed_sdui_page",
      "category":        "visualization",
      "label":           "Nhúng SDUI Page (Lakehouse/DataMatching chart)",
      "description":     "Fetch URL bên ngoài, render SduiPage inline",
      "icon":            "external-link",
      "requiredColumns": [],
      "optionalColumns": ["url", "queryParams"],
      "compatibleWith":  ["lakehouse::chart-page", "datamatch::pages"]
    }

[B] Admin trong screen designer → kéo widget này → cấu hình ConfigJson:
    {
      "providerCode":  "lakehouse",
      "operationKey":  "chart-page",
      "params":        { "code": "finance-daily" },
      "queryParams":   { "date": "{{filters.reportDate}}" },
      "height":        500
    }

[C] Screen layout endpoint /forms/screens/{module}/{screen}/layout
    trả widget này nguyên xi về cho FE.

[D] FE handler:
    case 'embed_sdui_page':
      const url = resolveProviderUrl(props.providerCode, props.operationKey, props.params);
      const data = await fetch(`${url}?${qs}`);
      return <SduiPageView page={data} />;     ← reuse renderer doc 48
```

---

## 3. Hướng dẫn FE để bật Mức 2

### 3.1 Resolve URL từ Provider Catalog

```ts
// src/lib/providerResolver.ts
interface Provider { code: string; baseUrl: string; }
interface Operation { operationKey: string; pattern: string; requiredParams: string[]; }

const providerCache: Record<string, Provider> = {};
const operationCache: Record<string, Operation> = {};

export async function resolveProviderUrl(
  providerCode: string,
  operationKey: string,
  params: Record<string, string>,
): Promise<string> {
  // Cache provider
  if (!providerCache[providerCode]) {
    const r = await fetch(`${BASE}/forms/admin/providers/${providerCode}`);
    providerCache[providerCode] = (await r.json()).data;
  }
  const provider = providerCache[providerCode];

  // Cache operation
  const key = `${providerCode}::${operationKey}`;
  if (!operationCache[key]) {
    const r = await fetch(`${BASE}/forms/admin/providers/${providerCode}/operations`);
    const ops = (await r.json()).data as Operation[];
    operationCache[key] = ops.find(o => o.operationKey === operationKey)!;
  }
  const op = operationCache[key];

  // Replace {param} placeholders trong pattern
  let path = op.pattern;
  for (const [k, v] of Object.entries(params))
    path = path.replace(`{${k}}`, encodeURIComponent(v));

  return `${provider.baseUrl}${path}`;
}
```

### 3.2 Renderer cho widget `embed_sdui_page`

```tsx
// src/components/widgets/EmbedSduiPage.tsx
'use client';
import { useEffect, useState } from 'react';
import { resolveProviderUrl } from '@/lib/providerResolver';
import { SduiPageView } from '@/components/sdui/SduiPageView';
import type { SduiPage } from '@/types/sdui';

interface Props {
  providerCode:  string;
  operationKey:  string;
  params:        Record<string, string>;
  queryParams?:  Record<string, string>;
  height?:       number;
}

export function EmbedSduiPageWidget({ providerCode, operationKey, params, queryParams, height }: Props) {
  const [page, setPage] = useState<SduiPage | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancel = false;
    (async () => {
      try {
        const baseUrl = await resolveProviderUrl(providerCode, operationKey, params);
        const qs = new URLSearchParams(queryParams ?? {}).toString();
        const url = qs ? `${baseUrl}?${qs}` : baseUrl;

        const r = await fetch(url);
        const json = await r.json();
        if (!cancel) {
          if (json.success) setPage(json.data);
          else setError(json.errorMessage ?? 'Fetch failed');
        }
      } catch (e: any) {
        if (!cancel) setError(e.message);
      }
    })();
    return () => { cancel = true; };
  }, [providerCode, operationKey, JSON.stringify(params), JSON.stringify(queryParams)]);

  if (error) return <div className="p-4 bg-red-50 text-red-700 text-sm">{error}</div>;
  if (!page) return <div className="p-4 text-slate-500">Loading chart...</div>;

  return (
    <div style={{ minHeight: height ?? 500 }}>
      <SduiPageView page={page} />
    </div>
  );
}
```

### 3.3 Map vào ComponentRenderer của screen

Trong renderer screen của DynamicForm, thêm case:

```tsx
// src/components/sdui/ComponentRenderer.tsx
import { EmbedSduiPageWidget } from '@/components/widgets/EmbedSduiPage';

switch (component.type) {
  case 'KpiCard':       return <KpiCard      props={component.props} />;
  case 'ProgressList':  return <ProgressList props={component.props} />;
  // ... 5 component types từ doc 48

  // NEW: cho widget catalog "embed_sdui_page"
  case 'embed_sdui_page':
    return <EmbedSduiPageWidget {...component.props} />;
}
```

---

## 4. Setup BE — Mức 1 (đã done) + Mức 2 (TODO)

### 4.1 Mức 1 — Provider/Operation đăng ký (DONE)

```bash
# Đã chạy 2026-06-08 trên server
curl -X POST https://192.168.100.60:8443/forms/admin/providers \
  -d '{ "code": "lakehouse", "displayName": "...", "baseUrl": "http://lakehouseservice:8080" }'

curl -X POST https://192.168.100.60:8443/forms/admin/providers/lakehouse/operations \
  -d '{
    "operationKey":   "chart-page",
    "displayName":    "Render Lakehouse Chart (SDUI page)",
    "pattern":        "/lakehouse/charts/{code}",
    "requiredParams": ["code"],
    "kind":           "Single"
  }'
```

→ Verify: `GET /forms/admin/providers/lakehouse/operations` trả entry `chart-page`.

### 4.2 Mức 2 — WidgetCatalog entry (TODO)

Hiện tại chưa có endpoint `POST /forms/admin/widget-catalog`. Cần BE viết:

**Option A — Quick API endpoint:**
```csharp
[HttpPost("widget-catalog")]
public async Task<IActionResult> Create([FromBody] CreateWidgetCatalogCommand cmd, CancellationToken ct)
{
    // ... validate + insert vào WidgetCatalog table
}
```

**Option B — Seed migration:**
Add seed data vào EF migration:
```csharp
migrationBuilder.InsertData(
    table: "WidgetCatalogs",
    columns: ["Id", "ChartType", "Category", "Label", "RowSchema", "RequiredColumnsJson", "OptionalColumnsJson", "CompatibleWithJson", "SortOrder"],
    values: [
        Guid.NewGuid(), "embed_sdui_page", "visualization",
        "Nhúng SDUI Page (Lakehouse chart)",
        "{}", "[]",
        "[\"providerCode\", \"operationKey\", \"params\", \"queryParams\", \"height\"]",
        "[\"lakehouse::chart-page\"]",
        100
    ]);
```

→ Sau khi seed, admin thấy widget này trong palette designer.

---

## 5. End-to-end flow sau khi xong Mức 2

```
[1] Admin tạo screen mới trong DynamicForm
    POST /forms/admin/screens

[2] Admin kéo widget "embed_sdui_page" từ palette → grid
    Set props:
      providerCode:  "lakehouse"
      operationKey:  "chart-page"
      params:        { "code": "finance-daily" }
      queryParams:   { "date": "{{filters.reportDate}}" }

[3] BE lưu FormScreenWidget với ConfigJson chứa props

[4] FE user mở screen
    GET /forms/screens/{module}/{screen}/layout
    → Layout chứa widget embed_sdui_page với ConfigJson

[5] FE renderer:
    a. Component type = "embed_sdui_page" → render <EmbedSduiPageWidget>
    b. Widget resolve URL qua resolveProviderUrl(...)
    c. Fetch URL → nhận SduiPage
    d. Pass SduiPage cho <SduiPageView> (doc 48)
    e. User thấy chart inline trong screen
```

---

## 6. Test ngay với Mức 1 (chưa có widget catalog)

FE có thể prototype EmbedSduiPageWidget ngay không đợi BE add WidgetCatalog:

```bash
# Sửa URL trong FE component → test render
const url = "https://192.168.100.60:8443/lakehouse/charts/finance-daily?demo=true";
// → Trả SduiPage với fake data
```

Nếu render ra dashboard đầy đủ (4 KPI + ProgressList + AlertList + FlowPipeline + ChartPie) → integration hoạt động đúng.

---

## 7. Pitfalls

| Pitfall | Cách tránh |
|---|---|
| Hardcode URL trong FE | Luôn qua `resolveProviderUrl` để đổi infra dễ |
| Cache provider/operation quá lâu | TTL 5 phút, hoặc invalidate khi admin update |
| Render nested SduiPage gây lag (chart trong chart trong chart) | Limit depth hoặc cảnh báo admin nếu nest >2 |
| FE bypass Provider Catalog, gõ tay URL | Code review reject; doc rõ ràng |
| Chart Path B trả 500 (raw tables placeholder) | Có error UI rõ — show error message từ response |
| CORS — lakehouse service không cho FE origin | nginx proxy `/lakehouse/charts/*` → service nội bộ |

---

## 8. Related docs

| Doc | Đọc khi |
|---|---|
| [41 — Loose Coupling Architecture](./41-loose-coupling-architecture.md) | Hiểu Provider/Operation catalog đầy đủ |
| [48 — FE Consume /dm/pages Chart](./48-frontend-consume-dm-pages-chart-guide.md) | Reuse `<SduiPageView>` renderer |
| [50 — BE Path B Lakehouse Chart](./50-add-new-lakehouse-chart-guide.md) | Tạo chart Path B mới |
| [51 — Charts System Overview](./51-charts-system-overview.md) | Big picture chart pipeline |

---

## 9. Changelog

- **2026-06-08** — Initial. Mức 1 (Provider/Operation đăng ký) đã DONE trên server. Mức 2 (WidgetCatalog + FE renderer) đề xuất, code mẫu TypeScript đầy đủ cho FE pickup.
