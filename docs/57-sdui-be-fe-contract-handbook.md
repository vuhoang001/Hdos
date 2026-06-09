# 57 — SDUI BE↔FE Contract Handbook

> **Mục đích:** Tài liệu chuẩn cho team triển khai. BE viết Consumer code (động ở server), FE render generic (động ở client). Đây là "hợp đồng" để 2 bên không cần biết nhau mà vẫn ăn khớp.
>
> **Audience:**
> - **BE dev** — đọc phần §3 (shared contract) + §5 (BE responsibilities) + §9 (naming/format)
> - **FE dev** — đọc toàn bộ. §6 (FE responsibilities) là nơi triển khai chính
> - **Tech lead / reviewer** — đọc §10 (review checklist) + §8 (versioning)
>
> Companion docs: [25 — SDUI engine](./25-sdui-server-driven-ui.md), [53 — Data Contract Engine](./53-chart-funnel-architecture.md), [54 — Walkthrough](./54-data-contract-walkthrough.md), [56 — Files explained](./56-data-contract-files-explained.md).

---

## 1. Nguyên tắc cốt lõi (1 sơ đồ + 3 dòng)

```
       BE (code, type-safe)              CONTRACT                 FE (generic renderer)
   ────────────────────────         ──────────────────         ────────────────────────
   - IDataSource<Row>               - Component vocabulary    - REGISTRY {type → comp}
   - IDataConsumer<Row, Page>       - Prop shape per widget   - <ScreenRenderer/>
   - DataContractGateway              (MANIFEST = SoT)        - 0 business logic
   - Output JSON ──────────────────────────────────────────►  spread {...props}
```

1. **BE quyết định nội dung & layout.** Viết `Consumer` C# build `SduiPage`.
2. **FE chỉ render.** Có 1 REGISTRY map `type` string → React component. Không biết "finance daily" là gì.
3. **Vocabulary chung = single source of truth.** MANIFEST (FE expose) liệt kê widget + prop shape. BE chỉ được dùng những thứ trong MANIFEST.

---

## 2. Vocabulary — widget catalog hiện tại

**Runtime nguồn:** `GET /api/manifest` (FE serve) → BE dev xem cái này khi viết Consumer.

**Code nguồn:** `fe/FOXAI-HDOSv2/src/components/registry/index.ts`

Snapshot tại thời điểm viết doc (16 widget):

| Type | Mục đích | Props chính |
|---|---|---|
| `KpiCard` | Thẻ số liệu KPI | `title`, `value`, `accent`, `hint`, `hintColor` |
| `ChartBar` | Biểu đồ cột | `data[{label,value}]`, `series?`, `title`, `color`, `unit` |
| `ChartLine` | Biểu đồ đường (trend) | `data[{label,value}]`, `series?`, `title`, `color` |
| `ChartArea` | Biểu đồ vùng | `data[{label,value}]`, `series?`, `title`, `color` |
| `ChartPie` | Tròn / donut | `data[{label,value}]`, `variant: "pie"\|"donut"`, `colors[]` |
| `DataTable` | Bảng generic | `columns[{key,title,render?,tagColors?}]`, `data[]`, `pageSize?` |
| `AlertBanner` | Banner thông báo | `message`, `description?`, `type: success\|info\|warning\|error` |
| `AlertList` | Danh sách cảnh báo realtime | `title`, `items[{code,text,patient,dept,time,severity}]`, `totalCount` |
| `ProgressList` | Top-N theo metric | `title`, `items[{label,value,secondaryValue?,color?}]`, `maxValue` |
| `BulletList` | Danh sách bullet | `title`, `items[{text,status,badge?}]` |
| `StatsSummary` | Block thống kê tóm tắt | `title`, `items[{label,value,color}]` |
| `FlowPipeline` | Pipeline N stage | `title`, `stages[{label,value,color?}]`, `footer?` |
| `WardBedGrid` | Grid giường bệnh | `wards[{code,total,occupied,...}]` |
| `OrRoomGrid` | Grid phòng mổ | `rooms[{code,procedure?,status,hint?}]` |
| `VoiceEMR` | EMR voice | `(panel-specific)` |
| `BaoCaoKhoaWidget` | Báo cáo khoa | `(custom)` |

> **Khi BE viết Consumer, BẮT BUỘC fetch `/api/manifest` (hoặc đọc file `registry/index.ts`) để biết widget nào tồn tại + props shape chính xác. KHÔNG được hard-code `type` không có trong MANIFEST.**

---

## 3. Shared contract — JSON envelope

### 3.1 Outer wrapper: `ApiResponse<T>` (chuẩn Hdos)

```json
{
  "success": true,
  "data":    { ... },        // payload thực sự
  "errorCode":   null,
  "errorMessage": null
}
```

Lỗi:
```json
{
  "success": false,
  "data":    null,
  "errorCode":   "CONTRACT.NOT_FOUND",
  "errorMessage": "Contract 'x.y.z' chưa đăng ký."
}
```

→ FE **luôn unwrap** `response.data` trước khi truyền vào renderer.

### 3.2 Inner payload: `SduiPage`

```json
{
  "code":        "finance-daily",
  "title":       "Tài chính theo ngày",
  "badge":       "Contract",
  "live":        true,
  "subtitle":    "qua DataContractGateway · 15:54 · Ngày 09/06/2026",
  "actions":     [{"label": "Xuất Excel", "variant": "default", "color": null}],
  "rows": [
    {
      "components": [
        { "type": "KpiCard",  "span": 6,  "props": {...} },
        { "type": "KpiCard",  "span": 6,  "props": {...} },
        { "type": "ChartPie", "span": 12, "props": {...} }
      ]
    },
    { "components": [ ... ] }
  ],
  "generatedAt": "2026-06-09T08:54:03Z"
}
```

| Field | Bắt buộc | Mô tả |
|---|---|---|
| `code` | ✅ | Slug định danh page (không trùng với contract code) |
| `title` | ✅ | Tiêu đề hiển thị |
| `badge` | optional | Chip nhỏ cạnh title (vd "Contract", "Live"). null = ẩn |
| `live` | ✅ | true = hiện chấm xanh "Live" |
| `subtitle` | optional | Dòng phụ dưới title |
| `actions[]` | ✅ (có thể `[]`) | Button góc trên phải. `variant: "primary"\|"default"\|"dashed"` |
| `rows[]` | ✅ | Mảng row. Mỗi row = grid Ant Design 24-col |
| `rows[].components[]` | ✅ | Widget trong row |
| `components[].type` | ✅ | **Phải khớp key REGISTRY** (case-sensitive) |
| `components[].span` | optional | 1-24. Null = chia đều phần còn lại |
| `components[].props` | ✅ | Spread thẳng vào React component |
| `generatedAt` | ✅ | ISO 8601 UTC. FE dùng để debug/cache |

### 3.3 JSON casing rule

| Vị trí | Casing | Ai enforce |
|---|---|---|
| Outer keys (`success`, `data`, ...) | camelCase | `JsonSerializerOptions.PropertyNamingPolicy = CamelCase` |
| `SduiPage` keys | camelCase | `[JsonPropertyName]` attribute trên record |
| `components[].type` | **PascalCase** (match React component name) | Hard-coded `[JsonDerivedType]` discriminator |
| `components[].props.*` | camelCase | TS interface FE expect camelCase |

→ **C# record dùng PascalCase, JsonSerializer convert sang camelCase tự động** (trừ `type` field giữ nguyên).

### 3.4 Type conventions cho `props.value` / `data`

| FE expect | BE phải gửi |
|---|---|
| number (cho ChartPie data, ProgressList value) | JSON number — KHÔNG phải string `"100"` |
| string format (cho KpiCard.value hiển thị) | string đã format — `"5.24 tỷ"`, `"19.0%"` |
| color | string hex `"#1677ff"` — không name (`"blue"`) |
| date | ISO 8601 `"2026-06-09"` hoặc `"2026-06-09T08:54:03Z"` |
| null vs undefined | null OK (FE handle `value ?? default`) |

---

## 4. Discovery endpoints — 2 chiều

```
FE → BE: "Đây là widget tôi nói được"
  GET /api/manifest                                    (FE serve, BE dev đọc)
  → { components: { KpiCard: { props: {...} }, ... } }

BE → FE: "Đây là contract/page tôi có"
  GET /lakehouse/contracts                             (BE serve, FE đọc)
  → [{ code: "finance.daily.row", displayName, schemaTypeName }, ...]

BE → FE: "Render contract code X qua consumer Y, source Z"
  GET /lakehouse/contracts/{code}/chart?source=...&consumer=chart
  → SduiPage (wrap trong ApiResponse)
```

→ Không có codegen build-time. Mọi sync là runtime + thủ công (dev đọc rồi viết code đúng).

---

## 5. BE responsibilities

### 5.1 Workflow viết Consumer mới (cho contract đã có widget)

1. **Fetch MANIFEST**: `curl http://fe-host/api/manifest` → biết widget có gì.
2. **Design page**: vẽ trên giấy/mockup, ghi rõ row nào dùng widget gì, span bao nhiêu.
3. **Implement `IDataConsumer<TRow, SduiPage>`**:
   ```csharp
   public sealed class MyChartConsumer : IDataConsumer<MyRow, SduiPage>
   {
       public string ConsumerCode => "chart";

       public async Task<SduiPage> ConsumeAsync(
           IAsyncEnumerable<MyRow> rows, DataContractQuery query, CancellationToken ct)
       {
           var list = await rows.ToListAsync(ct);
           // aggregate logic ở đây
           return new SduiPage(
               Code: "my-page",
               Title: "...",
               Badge: "Contract",
               Live: true,
               Subtitle: $"Ngày {DateTime.Today:dd/MM/yyyy}",
               Actions: [],
               Rows: [
                   new SduiRow([
                       new KpiCardComponent(Span: 6, Props: new KpiCardProps(
                           Title: "Tổng X", Value: "...", Accent: "#1677ff", Hint: null, HintColor: null)),
                       // ... thêm components
                   ]),
               ],
               GeneratedAt: DateTime.UtcNow);
       }
   }
   ```
4. **Register DI** (`DataContractsRegistration.cs`):
   ```csharp
   .AddDataConsumer<MyRow, SduiPage, MyChartConsumer>()
   ```
5. **Build + deploy BE**. Không touch FE.
6. **Smoke test**: `curl /lakehouse/contracts/{code}/chart?source=demo`.

### 5.2 Constraints BẮT BUỘC

| # | Rule | Lý do |
|---|---|---|
| 1 | **Chỉ dùng `SduiComponent` subtype có sẵn** trong `SduiComponent.cs` (`KpiCardComponent`, `ProgressListComponent`, ...) | Compile-time type safety; thêm widget mới = update FE trước (xem §6.1) |
| 2 | **`SourceCode` & `ConsumerCode` lowercase**, vd `"sql"`, `"demo"`, `"chart"`, `"csv"` | Query string `?source=demo` case-sensitive |
| 3 | **Props.* names phải khớp `KpiCardProps` record** (đã map sang camelCase qua `[JsonPropertyName]`) | FE expect chính xác key đó |
| 4 | **Không throw exception cho lỗi nghiệp vụ** — controller đã wrap qua `ApiResponse.Fail(...)` | Convention §8 CLAUDE.md |
| 5 | **Aggregation logic ở Consumer, KHÔNG ở Source** | Source = adapter input. Consumer = build output. Tách trách nhiệm |
| 6 | **Filter từ query string**: đọc qua `query.Filters["date"]` etc. | DataContractQuery normalize toàn bộ querystring |
| 7 | **Color phải hex** `"#RRGGBB"`. Không dùng "red", "blue" | FE component expect CSS color string |
| 8 | **`GeneratedAt` luôn UTC** (`DateTime.UtcNow`) | Tránh timezone bug khi cache |

### 5.3 Error code convention

| Khi nào | ErrorCode | HTTP | Message ví dụ |
|---|---|---|---|
| Contract không tồn tại | `CONTRACT.NOT_FOUND` | 404 | "Contract 'x.y.z' chưa đăng ký" |
| Source không tồn tại cho contract | `SOURCE.NOT_FOUND` | 404 | "Source 'sql' không có cho contract '...'" |
| Consumer không tồn tại | `CONSUMER.NOT_FOUND` | 404 | "Consumer 'chart' không có" |
| Filter sai format | `QUERY.INVALID` | 400 | "Tham số 'date' phải là YYYY-MM-DD" |
| Data trống (không phải lỗi) | success=true, rows=[] | 200 | (không lỗi) |
| Exception bất ngờ | `Server` | 500 | "An unexpected error occurred." (default middleware) |

### 5.4 Anti-patterns BE

- ❌ Hard-code `new SduiComponent("MyCustomType", ...)` mà không có subtype tương ứng.
- ❌ Trộn business logic vào Source (vd: source `IF month > 6 THEN ...`).
- ❌ Dùng `dynamic` cho props.
- ❌ Sửa `SduiPage` shape mà không update FE `ScreenConfig` type tương ứng.
- ❌ Build SduiPage trong Controller (phải trong Consumer).

---

## 6. FE responsibilities

### 6.1 Quy tắc maintain REGISTRY + MANIFEST

`src/components/registry/index.ts` là **single source of truth FE side**. Mọi widget mới đều phải:

1. Tạo `src/components/widgets/MyWidget.tsx`
2. Import vào `registry/index.ts` → thêm vào `REGISTRY`
3. Thêm prop schema vào `MANIFEST.components.MyWidget`
4. (Khuyến nghị) Update doc 34 (Widget Catalog)

**KHÔNG được:**
- Thêm component vào REGISTRY mà không có entry trong MANIFEST (BE không biết shape).
- Đổi key trong REGISTRY (vd rename `KpiCard` → `KpiTile`) mà không bump version + thông báo BE.
- Xóa widget đang dùng (cần check không Consumer nào còn dùng).

### 6.2 Generic page route

```
src/app/dashboards/[code]/page.tsx     (chưa có, FE cần tạo)
```

Chức năng:
1. Param `[code]` = contract code (vd `finance.daily.row`).
2. Đọc `?source=` & `?consumer=` từ URL (default: `source` lấy đầu tiên BE register, `consumer=chart`).
3. Fetch `/lakehouse/contracts/{code}/chart?source=...&consumer=chart` qua axios (`httpClient.ts`).
4. Unwrap `ApiResponse.data` → đưa vào `<ScreenRenderer config={data} loading={isFetching} />`.
5. Handle loading / error / empty.

Skeleton gợi ý (FE tự implement):

```tsx
"use client";
import { useEffect, useState } from "react";
import { useParams, useSearchParams } from "next/navigation";
import { ScreenRenderer } from "@/components/ScreenRenderer";
import { httpClient } from "@/infrastructure/http/httpClient";
import type { ScreenConfig } from "@/types/screen";

export default function DashboardPage() {
  const { code } = useParams<{ code: string }>();
  const search = useSearchParams();
  const source = search.get("source") ?? undefined;
  const consumer = search.get("consumer") ?? "chart";

  const [data, setData] = useState<ScreenConfig | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    setLoading(true); setError(null);
    const qs = new URLSearchParams();
    if (source) qs.set("source", source);
    qs.set("consumer", consumer);
    // Preserve các filter khác từ URL (date, department...)
    search.forEach((v, k) => { if (k !== "source" && k !== "consumer") qs.set(k, v); });

    httpClient
      .get(`/lakehouse/contracts/${code}/chart?${qs}`)
      .then(res => {
        if (res.data?.success) setData(res.data.data as ScreenConfig);
        else setError(res.data?.errorMessage ?? "Lỗi không xác định");
      })
      .catch(e => setError(e.message))
      .finally(() => setLoading(false));
  }, [code, source, consumer, search]);

  if (error) return <ErrorState message={error} />;
  if (!data && loading) return <LoadingState />;
  if (!data) return <EmptyState />;
  return <ScreenRenderer config={data} loading={loading} />;
}
```

### 6.3 Filter UI conventions

Khi page cần cho user lọc (date, source, department):

| Field | Component | Append vào URL |
|---|---|---|
| Source switch | Ant Design `<Select>` | `?source=demo\|sql` |
| Date | `<DatePicker>` | `?date=YYYY-MM-DD` |
| Department | `<Select>` đa lựa chọn | `?department=1,2,3` (comma-separated) |

→ BE chỉ đọc query string, không quan tâm UI. FE đẩy gì vào querystring → Source/Consumer xử lý.

### 6.4 Loading / Error / Empty states (bắt buộc)

| State | Khi | UI |
|---|---|---|
| Loading | First fetch | Skeleton từng row (giữ layout grid) |
| Refetch loading | Đang re-fetch sau khi đổi filter | `<ScreenRenderer loading={true}/>` — component tự render skeleton bên trong nó |
| Error | `success=false` hoặc network fail | Banner đỏ + error message + retry button |
| Empty (`rows: []`) | Data hợp lệ nhưng rỗng | "Không có dữ liệu trong khoảng đã chọn" + suggest đổi filter |
| Unknown widget type | `REGISTRY[type]` = undefined | Đã có sẵn — `<div>Widget chưa đăng ký: X</div>` (đỏ, dashed border) |

### 6.5 Anti-patterns FE

- ❌ Hard-code data trong widget (như `WidgetRenderer.tsx` hiện đang làm với DynamicForm — đó là legacy, không follow cho lakehouse).
- ❌ Transform shape của `props` trước khi spread (vd `<KpiCard title={data.props.title.toUpperCase()}/>`) — sai chỗ nào thì sửa ở BE Consumer, không sửa FE.
- ❌ Đặt business logic / nghiệp vụ vào widget component.
- ❌ Gắn API call vào widget — widget là dumb, page mới fetch.
- ❌ Đổi prop names ở widget mà không kiểm tra Consumer BE nào đang dùng.

---

## 7. Workflow thêm dashboard mới

### Case A — Dùng widget có sẵn (95% case)

```
┌──────────────────────────────────────────────────────────────┐
│ BE only — 6 bước                                             │
│                                                              │
│ 1. Fetch MANIFEST → chọn widget                             │
│ 2. Design layout (rows × spans)                              │
│ 3. Implement IDataSource<Row> (nếu chưa có)                  │
│ 4. Implement IDataConsumer<Row, SduiPage>                    │
│ 5. Register DI                                               │
│ 6. Build BE, deploy, curl test                               │
│                                                              │
│ FE: ZERO TOUCH. Page /dashboards/[code] tự render.           │
└──────────────────────────────────────────────────────────────┘
```

### Case B — Cần widget MỚI (chưa có trong MANIFEST)

```
┌──────────────────────────────────────────────────────────────┐
│ FE đi TRƯỚC — 9 bước                                         │
│                                                              │
│ FE (đi trước):                                               │
│ 1. Tạo src/components/widgets/MyWidget.tsx                   │
│ 2. Thêm vào REGISTRY (registry/index.ts)                     │
│ 3. Thêm prop schema vào MANIFEST                             │
│ 4. Update doc 34 (widget catalog)                            │
│ 5. PR + merge + deploy FE                                    │
│                                                              │
│ BE (sau khi FE done):                                        │
│ 6. Fetch /api/manifest → confirm widget mới có sẵn           │
│ 7. Update src/Services/.../Charts/Sdui/SduiComponent.cs:    │
│    thêm [JsonDerivedType(typeof(MyWidgetComponent), "MyWidget")] │
│    + record MyWidgetComponent + MyWidgetProps                │
│ 8. Implement Consumer dùng widget mới                        │
│ 9. Deploy BE                                                 │
└──────────────────────────────────────────────────────────────┘
```

**Quy tắc:** FE đi trước. Nếu BE dùng widget chưa có ở FE → BE trả JSON, FE render fallback "Widget chưa đăng ký". Không nguy hiểm, nhưng UX xấu.

---

## 8. Versioning policy

Hệ thống **CHƯA có** versioning runtime. Quy ước:

### 8.1 Additive change (an toàn)

- Thêm field optional vào props → Consumer cũ không gửi, Component dùng default. **Không cần version.**
- Thêm widget MỚI vào REGISTRY → BE cũ không dùng, không ảnh hưởng. **Không cần version.**
- Thêm filter optional (`?dept=`) → Source cũ ignore. **Không cần version.**

### 8.2 Breaking change (yêu cầu version)

- Đổi tên prop (`accent` → `accentColor`).
- Đổi kiểu prop (`value: string` → `value: number`).
- Xóa widget khỏi REGISTRY.
- Đổi shape `SduiPage` (vd: `rows` → `sections`).

→ **Quy trình:**
1. Tạo widget mới `KpiCard.v2` (giữ `KpiCard` cũ).
2. Update doc 34 đánh dấu `KpiCard` "deprecated, dùng v2 cho project mới".
3. Migrate dần Consumer cũ.
4. Khi không còn Consumer nào dùng `KpiCard` → xóa khỏi REGISTRY (release note).

### 8.3 Manifest version

`MANIFEST.version` hiện = `"1.0"`. Bump khi:
- Major: breaking change shape `ScreenConfig` envelope.
- Minor: thêm widget mới hoặc field mới (additive).
- Patch: fix doc/description không đổi behavior.

---

## 9. Naming & format chuẩn (cheat sheet)

| Loại | Format | Ví dụ |
|---|---|---|
| Contract code | lowercase, dot-separated | `finance.daily.row`, `patient.daily.new` |
| Source code | lowercase, single word | `sql`, `demo`, `view`, `rmq` |
| Consumer code | lowercase, kebab-case nếu nhiều từ | `chart`, `csv`, `form-prefill` |
| Widget type | PascalCase | `KpiCard`, `ChartPie`, `ProgressList` |
| Props key | camelCase | `title`, `accent`, `secondaryValue` |
| Color | hex 6-digit | `"#1677ff"`, `"#52c41a"`, `"#ff4d4f"` |
| Date | ISO 8601 | `"2026-06-09"` hoặc `"2026-06-09T08:54:03Z"` |
| Money (display) | string với đơn vị | `"5.24 tỷ"`, `"998 tr"` |
| Money (raw) | number | `5240000000` |
| Percent (display) | string + % | `"19.0%"` |
| Percent (raw) | number 0-100 | `19.0` |

### 9.1 Severity palette (chuẩn cho AlertList/AlertBanner)

| Severity | Hex | Khi nào |
|---|---|---|
| `critical` | `#ff4d4f` | Bệnh nhân nguy hiểm, action ngay |
| `warning` | `#faad14` | Cần chú ý, không khẩn cấp |
| `info` | `#1677ff` | Thông tin |
| `success` | `#52c41a` | Bình thường, đã hoàn thành |

### 9.2 Accent palette (chuẩn cho KpiCard)

| Mục đích | Hex |
|---|---|
| Primary (doanh thu, KPI chính) | `#1677ff` |
| Positive (tăng tốt) | `#52c41a` |
| Negative/alert (giảm, lỗi) | `#ff4d4f` |
| Warning (cần chú ý) | `#faad14` |
| Tertiary (số liệu phụ) | `#722ed1` `#13c2c2` `#eb2f96` |

→ Consumer C# nên dùng const, ví dụ trong code mỗi service tạo `class HdosColors { public const string Primary = "#1677ff"; ... }`.

---

## 10. Code review checklist

### 10.1 BE PR review (Consumer mới)

```
[ ] Consumer kế thừa IDataConsumer<TRow, SduiPage>?
[ ] ConsumerCode lowercase, không trùng consumer đã có cho contract đó?
[ ] Mọi component type dùng đều có trong SduiComponent.cs [JsonDerivedType]?
[ ] Props records dùng [JsonPropertyName] camelCase?
[ ] Color hex (không name), không hard-code Vietnamese trong code (cho i18n future)?
[ ] Aggregation logic ở Consumer, không leak xuống Source?
[ ] GeneratedAt = DateTime.UtcNow?
[ ] DI registration thêm vào DataContractsRegistration.cs?
[ ] Smoke test pass: curl /lakehouse/contracts/{code}/chart?source=demo trả 200?
[ ] Doc 54/56 update nếu contract mới?
```

### 10.2 FE PR review (Widget mới)

```
[ ] Component file ở src/components/widgets/MyWidget.tsx?
[ ] Đã import + add vào REGISTRY?
[ ] Đã add entry vào MANIFEST với prop schema đầy đủ (required flag, type, description)?
[ ] Props camelCase?
[ ] Component KHÔNG fetch data, KHÔNG có business logic?
[ ] Component handle loading prop?
[ ] Component handle dark mode (Tailwind dark: variants)?
[ ] Đã update doc 34 (widget catalog)?
[ ] Đã thông báo BE team có widget mới (kèm link MANIFEST entry)?
```

### 10.3 FE PR review (Generic page / dashboards)

```
[ ] Page route /dashboards/[code] hoặc tương đương generic?
[ ] Unwrap ApiResponse.data trước khi truyền vào ScreenRenderer?
[ ] Loading state (skeleton)?
[ ] Error state (banner + retry)?
[ ] Empty state (`rows: []`)?
[ ] Filter UI append vào URL query string, không state internal?
[ ] KHÔNG hard-code logic cho từng contract code?
```

---

## 11. End-to-end example: `finance.daily.row`

### 11.1 BE side

**Files** (đã có sẵn trong repo):

| File | Vai trò |
|---|---|
| `LakehouseService.Application/DataContracts/Schemas/Finance/FinanceDailyRow.cs` | Shape row |
| `LakehouseService.Application/DataContracts/Schemas/Finance/FinanceDailyContract.cs` | Contract declaration |
| `LakehouseService.Infrastructure/DataContracts/Sources/FinanceDailySqlSource.cs` | Source "sql" |
| `LakehouseService.Infrastructure/DataContracts/Sources/FinanceDailyDemoSource.cs` | Source "demo" |
| `LakehouseService.Infrastructure/DataContracts/Consumers/FinanceDailyChartConsumer.cs` | Consumer "chart" build SduiPage |
| `LakehouseService.Infrastructure/DataContracts/Registration/DataContractsRegistration.cs:27-33` | DI register |

### 11.2 Endpoint

```
GET /lakehouse/contracts/finance.daily.row/chart?source=demo
```

### 11.3 Response (verified `2026-06-09`)

```json
{
  "success": true,
  "data": {
    "code": "finance-daily",
    "title": "Tài chính theo ngày (DataContract)",
    "badge": "Contract",
    "live": true,
    "subtitle": "Qua DataContractGateway · 15:54 · Ngày 09/06/2026",
    "actions": [{"label": "Xuất Excel", "variant": "default", "color": null}],
    "rows": [
      {
        "components": [
          { "type": "KpiCard",  "span": 6, "props": { "title": "Tổng doanh thu", "value": "5.24 tỷ", "accent": "#1677ff", "hint": "VNĐ" } },
          { "type": "KpiCard",  "span": 6, "props": { "title": "Tổng giảm giá",  "value": "998 tr",  "accent": "#faad14", "hint": "19.0% DT" } }
        ]
      },
      {
        "components": [
          { "type": "ProgressList", "span": 16, "props": { "title": "Top 15 khoa...", "items": [...], "maxValue": 100 } },
          { "type": "AlertList",    "span": 8,  "props": { "title": "Khoa giảm giá cao", "items": [...], "totalCount": 2 } }
        ]
      },
      {
        "components": [
          { "type": "FlowPipeline", "span": 12, "props": { "title": "Dòng doanh thu", "stages": [...] } },
          { "type": "ChartPie",     "span": 12, "props": { "title": "Phân bổ theo loại HĐ", "data": [...] } }
        ]
      }
    ],
    "generatedAt": "2026-06-09T08:54:03.7548519Z"
  },
  "errorCode": null,
  "errorMessage": null
}
```

### 11.4 FE side — chỉ cần 1 page-thin

```tsx
// src/app/dashboards/[code]/page.tsx (FE chưa có, cần tạo theo §6.2)
// Browser truy cập:
//   /dashboards/finance.daily.row?source=demo
// → page fetch endpoint, render qua ScreenRenderer
```

→ **0 dòng code FE đặc thù cho finance daily.** Cùng page hoạt động cho `patient.daily.new` và mọi contract tương lai.

---

## 12. Tóm tắt 1 trang (in được dán lên tường)

```
╔══════════════════════════════════════════════════════════════╗
║  SDUI BE↔FE Contract — 5 dòng phải nhớ                       ║
║                                                              ║
║  1. BE viết Consumer. FE viết REGISTRY. Cả 2 độc lập.       ║
║  2. Vocabulary chung = /api/manifest. SoT, không guess.     ║
║  3. JSON shape = ApiResponse<SduiPage>. Casing chặt.        ║
║  4. Thêm dashboard: BE-only nếu widget đủ; FE-first nếu mới.║
║  5. Breaking change = widget v2. Additive = bump minor.     ║
║                                                              ║
║  Khi tắc: đọc doc 53 (architecture), 54 (walkthrough),      ║
║           56 (file map), 57 (contract — chính file này).    ║
╚══════════════════════════════════════════════════════════════╝
```

---

**Maintainer:** cập nhật doc này khi:
- MANIFEST có version mới
- Thêm/đổi widget trong REGISTRY
- Đổi `SduiPage` / `ApiResponse` envelope
- Thay đổi naming conventions

**Last updated:** 2026-06-09 — sau khi bỏ feature flag `DataContracts.EnableNewEndpoint`, endpoint `/lakehouse/contracts` mặc định ON.
