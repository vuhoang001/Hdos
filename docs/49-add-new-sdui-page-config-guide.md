# 49 — BE Tutorial: Thêm SDUI page mới (chart cho data lakehouse)

> **Mục đích.** Hướng dẫn BE developer **viết 1 file C#** kế thừa `SduiPageConfig`
> để FE có thêm 1 page chart-ready ở `GET /dm/pages/{code}`. Sau khi rebuild
> DataMatchingService, page tự xuất hiện trong `GET /dm/pages` — **FE không cần đổi gì**
> (giả định FE đã render generic theo doc 48).
>
> Worked example: `bed-occupancy` — dùng data ingest qua `with-auto-profile` từ
> view `api.bed_occupancy`.
>
> **Tài liệu liên quan:**
> - [doc 25](./25-sdui-server-driven-ui.md) — khái niệm SDUI vs Dashboard Engine
> - [doc 44](./44-unified-ingest-pipeline.md) — luồng ingest tổng
> - [doc 45](./45-lakehouse-auto-sourceprofile.md) — MVP B `with-auto-profile`
> - [doc 46](./46-playbook-add-source-data.md) — playbook onboard nguồn data
> - [doc 47](./47-test-mvp-b-lakehouse-view.md) — QA verify pipeline
> - [doc 48](./48-frontend-consume-dm-pages-chart-guide.md) — **FE consumer guide** (đọc kèm để hiểu shape FE expect)

---

## 0. TL;DR — 5 bước

```
[1] Verify data đã ingest                          ← curl /dm/sources + /dm/records
[2] Tạo 1 file .cs kế thừa SduiPageConfig          ← copy template §4
[3] Đăng ký 1 dòng DI                              ← DependencyInjection.cs
[4] dotnet build                                   ← verify compile
[5] FE gọi GET /dm/pages/{code}                    ← test cuối
```

Worked example bed-occupancy đã được implement: `Sdui/Pages/BedOccupancySduiConfig.cs`. Đọc §5 đối chiếu lý do từng quyết định.

---

## 1. Trước khi viết — checklist tiền điều kiện

| # | Check | Cách verify |
|---|---|---|
| 1 | RecordType bạn muốn chart đã được ingest? | `curl /dm/records?recordType=<rt>&limit=1` → có data |
| 2 | SourceProfile đã enroll? | `curl /dm/sources` → có entry `sourceSystem` + `recordType` khớp |
| 3 | Biết canonical field names? | Xem `mappings` trong SourceProfile (vd `OccupiedBedCount`, `KhoaDieuTri`) |
| 4 | Biết shape của value? (int/string/datetime/null) | Xem `canonicalPayload` của 1 record sample |

Nếu thiếu 1-3 → quay lại doc 46/47 onboard data trước.

### Ví dụ: bed-occupancy

```bash
BASE=https://192.168.100.60:8443

# (1) Record có chưa?
curl -sk "$BASE/dm/records?recordType=bed-occupancy&limit=1" | jq '.data | length'
# > 0

# (2) SourceProfile
curl -sk "$BASE/dm/sources" | jq '.data[] | select(.recordType=="bed-occupancy")'

# (3) Mappings (canonical names) — quan trọng nhất
curl -sk "$BASE/dm/sources" | jq '.data[] | select(.recordType=="bed-occupancy") | .mappings'

# Kết quả mẫu:
# {
#   "date": "Date",                          ← chú ý kiểu: ISO datetime, không phải date thuần
#   "department_id": "DepartmentId",
#   "department_name": "KhoaDieuTri",
#   "planned_bed_count": "PlannedBedCount",
#   "occupied_bed_count": "OccupiedBedCount",
#   ...
# }

# (4) Shape 1 record để biết kiểu giá trị
curl -sk "$BASE/dm/records?recordType=bed-occupancy&limit=1" | jq '.data[0].canonicalPayload | fromjson'
# {
#   "Date": "2026-06-05T00:00:00.0000000",   ← ISO datetime (DateOnly.TryParse không match — phải fallback DateTime)
#   "OccupancyRatio": 0.04,                   ← fractional, *100 khi hiển thị %
#   "OccupiedBedCount": 1,
#   ...
# }
```

> **Pitfall 1 — DateOnly.TryParse fail trên ISO datetime:** Helper `Date(row, key)` ở `SduiPageConfig` base class chỉ dùng `DateOnly.TryParse` → fail nếu value là `"2026-06-05T00:00:00.0000000"`. Phải viết helper riêng try `DateOnly` rồi fallback `DateTime` (xem §5.3).

---

## 2. Hiểu engine flow trước khi viết

```
HTTP GET /dm/pages/{code}
        │
        ▼
PagesController.Get(code, sourceSystem?, date?, ct)
        │
        ▼
SduiEngine.ExecuteAsync(code, ...)
        │
        ├─ [1] Lookup SduiPageConfig bằng code  ← DI registry
        ├─ [2] reportDate = date ?? Today (UTC)
        ├─ [3] FOREACH rt in config.RecordTypes:
        │         raw  = await _records.GetMatchedAsync(sourceSystem, rt, null, null, ct)
        │         data[rt] = ParsePayloads(raw)    ← parse JSON canonicalPayload → Dict
        │
        ▼
config.BuildPage(data, reportDate)   ← BẠN IMPLEMENT
        │
        ▼
Return SduiPage trong ApiResponse envelope
```

**4 điểm cần nhớ:**

1. `config.RecordTypes` là input cho engine fetch — bạn liệt kê những record types nào cần.
2. Engine **fetch tuần tự** (đã fix race condition). Bạn KHÔNG cần lo concurrency.
3. `data[rt]` là `List<Dictionary<string, JsonElement>>` — mỗi dict là 1 row với key = canonical field name.
4. `BuildPage` synchronous — không async, không I/O. Pure compute từ `data` + `reportDate`.

---

## 3. Layout cheat sheet — 5 component types

Trước khi code, paint mental model layout. Mỗi `SduiPage` chứa `Rows[]`. Mỗi `Row` là **24-col grid** chứa components có `span` (tổng ≤ 24).

| Component | Span gợi ý | Best for |
|---|---|---|
| `KpiCard` | 6 (4 cards / row) hoặc 8 (3 cards / row) | Số liệu tổng |
| `ProgressList` | 12, 16, 24 | Danh sách tỷ lệ theo nhóm |
| `AlertList` | 8, 12, 24 | Cảnh báo / outlier |
| `FlowPipeline` | 12, 24 | State machine / luồng |
| `ChartPie` | 8, 12 | Phân bổ tỷ lệ donut/pie |

Layout pattern phổ biến (3 row):

```
Row 1 — KPI: [Card6] [Card6] [Card6] [Card6]                ← summary numbers
Row 2 — Detail: [ProgressList 16]  [AlertList 8]              ← chi tiết theo nhóm + outlier
Row 3 — Visualization: [FlowPipeline 12]  [ChartPie 12]       ← chart
```

Chi tiết shape mỗi component: doc [48 §6](./48-frontend-consume-dm-pages-chart-guide.md#6-5-component-types--full-schema--sample).

---

## 4. Template `SduiPageConfig`

```csharp
// src/Services/DataMatchingService/DataMatchingService.Application/Sdui/Pages/<YourPage>SduiConfig.cs
using System.Text.Json;

namespace Hdos.DataMatchingService.Application.Sdui.Pages;

public sealed class YourPageSduiConfig : SduiPageConfig
{
    // [1] Page code dùng trong URL: GET /dm/pages/<code>
    public override string Code => "your-page-code";

    // [2] Record types cần fetch (engine sẽ tự query StagingRecord matched)
    public override IReadOnlyList<string> RecordTypes => ["your-record-type"];

    // [3] Build layout + data
    public override SduiPage BuildPage(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate)
    {
        var rows = data.GetValueOrDefault("your-record-type", []);

        // [3a] Empty-data guard — TUYỆT ĐỐI ĐỪNG SKIP
        if (rows.Count == 0)
            return BuildEmpty(reportDate);

        // [3b] (Optional) filter theo reportDate
        // var todayRows = rows.Where(r => DateOnlyOf(r, "Date") == reportDate).ToList();

        // [3c] Aggregate
        int total = rows.Count;
        // ...

        // [3d] Compose
        return new SduiPage(
            Code:        Code,
            Title:       "Title cho FE hiển thị",
            Badge:       "Mới nhất",          // null = ẩn
            Live:        true,                  // FE bật polling khi true
            Subtitle:    $"Cập nhật {DateTime.UtcNow:HH:mm} · {total} record",
            Actions:     [
                new("Xuất Excel", "default", null),
            ],
            Rows:        [
                BuildKpiRow(/* ... */),
                BuildDetailRow(rows),
                BuildChartRow(rows),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    // ─── Row builders ───────────────────────────────────────

    private static SduiRow BuildKpiRow(/* aggregated values */) =>
        new([
            new KpiCardComponent(6, new KpiCardProps(
                Title:     "Tổng X",
                Value:     0,            // int hoặc string
                Accent:    "#1677ff",     // null = default
                Hint:      "đơn vị",
                HintColor: null)),
            // ... 3 KpiCard nữa
        ]);

    private static SduiRow BuildDetailRow(List<Dictionary<string, JsonElement>> rows)
    {
        var items = rows
            .GroupBy(r => Str(r, "GroupField") ?? "(Khác)")
            .Select(g => new ProgressItem(
                Label:          g.Key,
                Value:          g.Count(),
                SecondaryValue: null,
                Color:          null))
            .ToList();

        return new SduiRow([
            new ProgressListComponent(24, new ProgressListProps(
                Title:         "Detail",
                HeaderAction:  null,
                MaxValue:      rows.Count,
                Items:         items,
                FooterActions: null)),
        ]);
    }

    private static SduiRow BuildChartRow(List<Dictionary<string, JsonElement>> rows)
    {
        var pieData = rows
            .GroupBy(r => Str(r, "CategoryField") ?? "Khác")
            .Select(g => new ChartPieData(g.Key, g.Count()))
            .ToList();

        return new SduiRow([
            new ChartPieComponent(24, new ChartPieProps(
                Title:   "Phân bổ",
                Height:  280,
                Variant: "donut",
                Legend:  true,
                Data:    pieData,
                Colors:  null)),
        ]);
    }

    // ─── Helpers riêng ──────────────────────────────────────

    private SduiPage BuildEmpty(DateOnly reportDate) =>
        new(Code, "Title", "Trống", false,
            $"Chưa có dữ liệu cho ngày {reportDate:dd/MM/yyyy}",
            [], [], DateTime.UtcNow);

    // Cho data lakehouse có Date dạng ISO datetime — base class Date() chỉ TryParse DateOnly
    private static DateOnly? DateOnlyOf(Dictionary<string, JsonElement> row, string key)
    {
        var s = Str(row, key);
        if (string.IsNullOrEmpty(s)) return null;
        if (DateOnly.TryParse(s, out var d))    return d;
        if (DateTime.TryParse(s, out var dt))   return DateOnly.FromDateTime(dt);
        return null;
    }
}
```

### Đăng ký DI

Thêm 1 dòng trong `src/Services/DataMatchingService/DataMatchingService.Application/DependencyInjection.cs`:

```csharp
services.AddSingleton<SduiPageConfig, YourPageSduiConfig>();
```

(Bên dưới dòng đăng ký `ExecutiveSduiConfig`.)

### Rebuild + test

```bash
docker compose up -d --build datamatchingservice

curl -k "https://localhost:8443/dm/pages" | jq
# → ["bed-occupancy", "executive", "your-page-code"]    ← page mới xuất hiện

curl -k "https://localhost:8443/dm/pages/your-page-code" | jq '.data.title'
```

---

## 5. Worked example — `BedOccupancySduiConfig`

File thật: `src/Services/DataMatchingService/DataMatchingService.Application/Sdui/Pages/BedOccupancySduiConfig.cs` (đã merge).

### 5.1 Decisions

| Quyết định | Giá trị | Lý do |
|---|---|---|
| `Code` | `"bed-occupancy"` | Match `recordType` cho dễ nhớ |
| `RecordTypes` | `["bed-occupancy"]` | Chỉ 1 source data |
| Filter theo ngày | Có (`DateOnlyOf(r, "Date") == reportDate`) | Data có nhiều snapshot/ngày; tránh sum trùng |
| Fallback khi không khớp ngày | Dùng tất cả rows | Tránh FE thấy trang trống khi `date` query không đúng |
| Top N khoa | 15 ở ProgressList | Tránh page quá dài; sorted theo BOR desc |
| Alert threshold | BOR ≥ 90% critical, 75-89% warning | Convention healthcare |

### 5.2 Layout (3 row)

```
Row 1: [KpiCard 6: Tổng giường] [Occupied 6] [Available 6] [BOR% 6]
Row 2: [ProgressList 16: BOR theo khoa Top 15] [AlertList 8: Khoa quá tải]
Row 3: [FlowPipeline 12: Phân bổ trạng thái] [ChartPie 12: Donut Occupied/Available/Disabled]
```

### 5.3 Pitfall đã solve

#### Date là ISO datetime, không DateOnly

```csharp
// SduiPageConfig base helper — không xử lý "2026-06-05T00:00:00.0000000"
protected static DateOnly? Date(Dictionary<string, JsonElement> row, string key) =>
    DateOnly.TryParse(Str(row, key), out var d) ? d : null;

// Override riêng trong BedOccupancySduiConfig — fallback DateTime
private static DateOnly? DateOnlyOf(Dictionary<string, JsonElement> row, string key)
{
    var s = Str(row, key);
    if (string.IsNullOrEmpty(s)) return null;
    if (DateOnly.TryParse(s, out var d))    return d;
    if (DateTime.TryParse(s, out var dt))   return DateOnly.FromDateTime(dt);
    return null;
}
```

#### `OccupancyRatio` fractional vs percent

Data có `"OccupancyRatio": 0.04` = 4%. Trong implementation không dùng field này — thay vào đó compute BOR từ `OccupiedBedCount / ActualBedCount` để control format chính xác.

### 5.4 Test trên server

```bash
# Sau khi rebuild + restart
curl -k "https://192.168.100.60:8443/dm/pages/bed-occupancy" -w "\nHTTP %{http_code}\n" | jq '
  .data | {
    title,
    badge,
    rowCount: (.rows|length),
    kpiCount: ([.rows[0].components[]] | length),
    componentTypes: [.rows[].components[].type] | unique
  }
'
```

Expected:
```json
{
  "title": "Công suất giường bệnh",
  "badge": "Đúng ngày",
  "rowCount": 3,
  "kpiCount": 4,
  "componentTypes": ["AlertList", "ChartPie", "FlowPipeline", "KpiCard", "ProgressList"]
}
```

---

## 6. 8 nguyên tắc khi viết SduiPageConfig

### 1. Empty-data guard luôn ở đầu `BuildPage`

```csharp
if (rows.Count == 0) return BuildEmpty(reportDate);
```

Nếu không, các phép `Sum`/`Average`/`GroupBy` có thể OK nhưng `Round(0/0, 1)` → NaN → JSON serialize fail.

### 2. Đừng tin field name — luôn check `Str()` / `Int()` cho null

```csharp
// SAI: row["KhoaDieuTri"] — throw KeyNotFoundException
// ĐÚNG: Str(row, "KhoaDieuTri") ?? "(không tên)"
```

Mappings có thể đổi (vd typo `KhoaDieuTri` → `KhoaDieuTri`); guard `??` an toàn hơn.

### 3. Helper `Date()` base class chỉ chấp nhận DateOnly format

Nếu data có `Date` kiểu ISO datetime → viết helper riêng `DateOnlyOf` (xem §5.3).

### 4. Filter theo `reportDate` rồi fallback toàn bộ nếu rỗng

```csharp
var todayRows = rows.Where(r => DateOnlyOf(r, "Date") == reportDate).ToList();
var effective = todayRows.Count > 0 ? todayRows : rows;
```

→ Page không bao giờ trống chỉ vì user query nhầm ngày.

### 5. Đừng `await` trong `BuildPage` — pure sync

`BuildPage` là pure compute. Mọi I/O đã xong ở engine. Nếu cần fetch thêm data ngoài `RecordTypes` → khai báo thêm vào `RecordTypes`, không tự inject repo và await.

### 6. Polymorphic JSON — luôn dùng record types có sẵn

Đừng tự định nghĩa `SduiComponent` mới mà chưa thêm `[JsonDerivedType]` ở `SduiComponent.cs`. System.Text.Json không biết serialize → 500.

### 7. `Value: object?` ở `KpiCardProps` — mix int/string OK

```csharp
new KpiCardProps(Value: 150, ...)            // int
new KpiCardProps(Value: $"{bor}%", ...)       // string
```

System.Text.Json handle được — nhưng đừng dùng custom struct/class.

### 8. Reproducible khi data thay đổi

Nếu cần test edge case — ingest 1 record giả qua `POST /dm/ingest/json` và verify response thay vì manual debug.

---

## 7. Convert sang Dashboard pattern (lighter)

`DashboardEngine` (cùng cha mẹ) dùng `DashboardSection` với 4 type đơn giản hơn:

| | SDUI (`/dm/pages/{code}`) | Dashboard (`/dm/dashboards/{code}` — chưa expose) |
|---|---|---|
| Section types | 5 (KpiCard, ProgressList, AlertList, FlowPipeline, ChartPie) | 4 (KpiGrid, PieChart, BarChart, Table) |
| Layout | 24-col grid với rows + span | Linear sections, không grid |
| Style | Server quyết màu/accent | Client tự pick |
| Phù hợp | Dashboard executive phức tạp | Báo cáo đơn giản, BI-like |

**Để mở endpoint `/dm/dashboards/{code}`:** thêm 1 controller (~20 dòng) — xem [doc 48 §16.2](./48-frontend-consume-dm-pages-chart-guide.md#16-mở-rộng--thêm-chart-cho-data-lakehouse-mới) (skeleton có sẵn).

`DashboardConfig` template:
```csharp
public sealed class YourDashboardConfig : DashboardConfig
{
    public override string Code  => "your-code";
    public override string Title => "Title";
    public override IReadOnlyList<string> RecordTypes => ["your-rt"];

    public override List<DashboardSection> BuildSections(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate)
    {
        var rows = data.GetValueOrDefault("your-rt", []);
        return [
            new KpiGridSection("summary", "Tổng quan", [
                new KpiItem("Tổng", rows.Count, "record", "number"),
            ]),
            // BarChart, PieChart, Table — xem DashboardSection.cs
        ];
    }
}
```

---

## 8. Test checklist sau khi viết

```bash
# [1] Compile
dotnet build src/Services/DataMatchingService/DataMatchingService.API

# [2] Restart container
docker compose up -d --build datamatchingservice
docker compose logs -f datamatchingservice | head -20

# [3] Page có trong list?
curl -k "https://localhost:8443/dm/pages" | jq '.data | index("your-code")'   # not null

# [4] Render OK — 200, không 500
curl -k "https://localhost:8443/dm/pages/your-code" -w "\nHTTP %{http_code}\n" | head -5

# [5] Component types đúng
curl -k "https://localhost:8443/dm/pages/your-code" | jq '[.data.rows[].components[].type] | unique'

# [6] FE render (mở browser tab dashboard)
```

Nếu 500 → check log: `docker compose logs datamatchingservice | grep -A 30 -i exception | tail -50`.

---

## 9. Common errors & fix

| Error | Nguyên nhân | Fix |
|---|---|---|
| 500 + "A second operation was started on this context" | Bạn tự `await Task.WhenAll` trong `BuildPage` | Đừng — engine đã fetch hộ. `BuildPage` pure sync |
| 500 + `KeyNotFoundException` | Access `row["FieldX"]` direct | Dùng `Str(row, "FieldX") ?? default` |
| 500 + `DivideByZeroException` rare nhưng có | `dangDieuTri * 100.0 / 0` không throw (ra Infinity) — nhưng JSON serialize Infinity throw | Guard `if (denominator > 0)` |
| 404 — Page not found | Quên đăng ký DI / typo code | Verify `GET /dm/pages` |
| Component không render trên FE | Tự thêm component type mới mà chưa thêm `[JsonDerivedType]` | Sửa `SduiComponent.cs` thêm `[JsonDerivedType(typeof(NewComponent), "NewType")]` |
| Date filter mất hết data | Data có `Date` ISO datetime, dùng `Date()` base helper | Viết helper `DateOnlyOf` (§5.3) |
| Value KpiCard "NaN" | Phép chia `0/0` hoặc Infinity | Math.Round guard `denominator > 0` |

---

## 10. Khi nào cần đụng vào base class `SduiPageConfig`?

Hiếm khi. Nhưng có 3 case:

| Case | Hành động |
|---|---|
| Helper chung cho nhiều page (vd `DateOnlyOf`, `Pct`, `BOR`) | Thêm `protected static` method vào `SduiPageConfig.cs` |
| Cần access JWT / current user trong BuildPage | Đụng base — nhưng cân nhắc kiến trúc lại; thường nên filter ở `_records.GetMatchedAsync` |
| Polymorphic types mới | Sửa `SduiComponent.cs` thêm `[JsonDerivedType]` + record component + props |

---

## 11. Related docs

| Doc | Đọc khi |
|---|---|
| [25 — SDUI](./25-sdui-server-driven-ui.md) | Khái niệm + so sánh Dashboard Engine |
| [44 — Unified Ingest Pipeline](./44-unified-ingest-pipeline.md) | Hiểu data đến từ đâu |
| [45 — Auto-Enroll SourceProfile](./45-lakehouse-auto-sourceprofile.md) | Setup nguồn lakehouse |
| [46 — Playbook Add Source Data](./46-playbook-add-source-data.md) | Step-by-step onboard nguồn data |
| [47 — Test MVP B Lakehouse View](./47-test-mvp-b-lakehouse-view.md) | QA verify pipeline |
| [48 — FE Consume `/dm/pages`](./48-frontend-consume-dm-pages-chart-guide.md) | Tận đầu kia của pipeline — FE render shape |

---

## 12. Changelog

- **2026-06-08** — Initial. Cover `SduiPageConfig` lifecycle, 5 component types, template + worked example `BedOccupancySduiConfig`, 8 nguyên tắc, common errors.
