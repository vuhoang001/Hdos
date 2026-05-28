# 26 — Dashboard Engine: Hướng dẫn từ đầu đến cuối

Tài liệu này hướng dẫn toàn bộ luồng — từ cách dữ liệu đi vào hệ thống, cách viết một
dashboard mới, đến cách frontend gọi và render. Đọc theo thứ tự từ trên xuống.

---

## Phần 1 — Tổng quan luồng dữ liệu

```
[HIS / bên thứ 3]
      │
      │ POST /dm/sources          (1 lần, đăng ký ánh xạ field)
      │ POST /dm/ingest/json      (lặp lại theo lịch hoặc real-time)
      ▼
[DataMatchingService — PostgreSQL]
  StagingRecord
    SourceSystem  = "his-01"
    RecordType    = "benh-nhan-noi-tru"
    RawPayload    = JSON gốc từ HIS
    CanonicalPayload = JSON đã chuẩn hóa (sau khi apply mappings)
    Status        = Matched
      │
      │ GET /dm/dashboards/m02
      ▼
[DashboardEngine]
  1. Tìm M02DashboardConfig trong registry
  2. Fetch StagingRecord theo RecordTypes (song song)
  3. Parse CanonicalPayload → List<Row>
  4. Gọi config.BuildSections(data, reportDate)
  5. Trả về sections[]
      │
      ▼
[Frontend Next.js]
  <DashboardRenderer sections={sections} />
  switch(type) → render đúng component
```

---

## Phần 2 — Cấu trúc code (đọc file nào trước)

```
Dashboard/
  DashboardSection.cs      ← [ĐỌC TRƯỚC] định nghĩa các loại section
  DashboardConfig.cs       ← [ĐỌC THỨ 2] abstract base, helpers
  DashboardEngine.cs       ← [ĐỌC THỨ 3] orchestrator
  Configs/
    M02DashboardConfig.cs  ← [VÍ DỤ] dashboard thực tế
```

---

## Phần 3 — Chi tiết từng file

### 3.1 `DashboardSection.cs` — Các loại section

Đây là **ngôn ngữ chung** giữa backend và frontend. Frontend chỉ cần biết 4 loại này.

```csharp
// Attribute này cho phép serialize List<DashboardSection> hỗn hợp nhiều loại.
// Mỗi object trong JSON sẽ có thêm field "type" để frontend biết render gì.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KpiGridSection),  "kpi-grid")]
[JsonDerivedType(typeof(PieChartSection), "pie-chart")]
[JsonDerivedType(typeof(BarChartSection), "bar-chart")]
[JsonDerivedType(typeof(TableSection),    "table")]
public abstract record DashboardSection(string Id, string Title);
```

**Bốn section hiện có:**

| Class | `type` trong JSON | Dùng để render |
|-------|-------------------|----------------|
| `KpiGridSection` | `"kpi-grid"` | Các số dạng card (BOR, ALOS, ...) |
| `PieChartSection` | `"pie-chart"` | Biểu đồ tròn tỷ lệ % |
| `BarChartSection` | `"bar-chart"` | Biểu đồ cột so sánh |
| `TableSection` | `"table"` | Bảng danh sách chi tiết |

**Chi tiết từng loại:**

```csharp
// KPI Grid — danh sách card số liệu
public sealed record KpiItem(
    string Label,   // "Đang điều trị"
    double Value,   // 12
    string? Unit,   // "bệnh nhân"
    string Format); // "number" | "percent" | "currency" | "days"

public sealed record KpiGridSection(string Id, string Title, List<KpiItem> Items)
    : DashboardSection(Id, Title);
```

```csharp
// Pie Chart — biểu đồ tròn
public sealed record PieSlice(string Label, int SoLuong, double PhanTram);

public sealed record PieChartSection(string Id, string Title, List<PieSlice> Data)
    : DashboardSection(Id, Title);
```

```csharp
// Bar Chart — biểu đồ cột
public sealed record BarItem(string Label, int SoLuong);

public sealed record BarChartSection(string Id, string Title, List<BarItem> Data)
    : DashboardSection(Id, Title);
```

```csharp
// Table — bảng danh sách
public sealed record TableColumn(
    string Key,    // "trangThai" — key trong rows[]
    string Label,  // "Trạng thái" — tiêu đề cột
    string Type);  // "string" | "number" | "currency" | "date" | "badge"

public sealed record TableSection(
    string Id, string Title,
    List<TableColumn> Columns,
    List<Dictionary<string, object?>> Rows) // mỗi row là dict key→value
    : DashboardSection(Id, Title);
```

---

### 3.2 `DashboardConfig.cs` — Abstract base

```csharp
public abstract class DashboardConfig
{
    // Code dùng trong URL: GET /dm/dashboards/{Code}
    public abstract string Code { get; }

    // Tên hiển thị trong response
    public abstract string Title { get; }

    // Danh sách RecordType cần fetch từ StagingRecord
    // Engine sẽ fetch TẤT CẢ các loại này song song
    public abstract IReadOnlyList<string> RecordTypes { get; }

    // Engine gọi hàm này sau khi fetch + parse xong data
    // data[recordType] = danh sách rows đã parse từ CanonicalPayload
    public abstract List<DashboardSection> BuildSections(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate);

    // Helpers đọc giá trị từ một row (Dictionary<string, JsonElement>)
    protected static string?  Str(row, key)  // đọc string
    protected static int      Int(row, key)  // đọc int, default 0
    protected static decimal  Dec(row, key)  // đọc decimal, default 0
    protected static DateOnly? Date(row, key) // parse "yyyy-MM-dd" → DateOnly
}
```

---

### 3.3 `DashboardEngine.cs` — Orchestrator

Engine không biết gì về M02 hay M03. Nó chỉ biết 4 bước:

```
1. Lookup config theo code
         ↓
2. Fetch StagingRecord theo config.RecordTypes (Task.WhenAll = song song)
         ↓
3. Parse JSON (CanonicalPayload → List<Dictionary<string,JsonElement>>)
         ↓
4. Gọi config.BuildSections(data, date)  →  trả List<DashboardSection>
```

```csharp
public sealed class DashboardEngine(
    IStagingRecordRepository records,
    IEnumerable<DashboardConfig> configs)  // ← nhận TẤT CẢ config đã đăng ký trong DI
{
    private readonly Dictionary<string, DashboardConfig> _registry =
        configs.ToDictionary(c => c.Code); // { "m02": M02Config, "m03": M03Config, ... }

    public async Task<DashboardResult?> ExecuteAsync(
        string code, string? sourceSystem, DateOnly? date, CancellationToken ct)
    {
        if (!_registry.TryGetValue(code, out var config)) return null; // 404

        var reportDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // Bước 2: fetch song song
        var fetched = await Task.WhenAll(
            config.RecordTypes.Select(async rt => {
                var raw = await _records.GetMatchedAsync(sourceSystem, rt, null, null, ct);
                return (RecordType: rt, Rows: ParsePayloads(raw));
            }));

        // Bước 3+4: parse xong → gọi config
        var data     = fetched.ToDictionary(x => x.RecordType, x => x.Rows);
        var sections = config.BuildSections(data, reportDate);

        return new DashboardResult(code, config.Title, reportDate, DateTime.UtcNow, sections);
    }
}
```

---

### 3.4 `M02DashboardConfig.cs` — Ví dụ dashboard thực tế

```csharp
public sealed class M02DashboardConfig : DashboardConfig
{
    public override string Code  => "m02";
    public override string Title => "Trực quan Nội trú";

    // Engine sẽ fetch 2 record types này song song
    public override IReadOnlyList<string> RecordTypes =>
        ["benh-nhan-noi-tru", "cau-hinh-giuong"];

    public override List<DashboardSection> BuildSections(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate)
    {
        // Lấy rows từ dict, nếu không có thì dùng list rỗng
        var patients = data.GetValueOrDefault("benh-nhan-noi-tru", []);
        var bedCfg   = data.GetValueOrDefault("cau-hinh-giuong",   []);

        // Trả về 4 sections theo thứ tự hiển thị
        return [
            BuildKpiGrid(patients, bedCfg, reportDate),  // card số liệu
            BuildDoiTuongKcb(patients),                  // biểu đồ tròn
            BuildTopIcd(patients),                       // biểu đồ cột
            BuildBenhNhanTable(patients),                // bảng danh sách
        ];
    }

    // Đọc field từ row dùng helpers kế thừa từ DashboardConfig
    private KpiGridSection BuildKpiGrid(patients, bedCfg, reportDate)
    {
        int dangDieuTri = patients.Count(r => Str(r, "TrangThai") == "DangNoiTru");
        int vaoVien     = patients.Count(r => Date(r, "NgayNhap") == reportDate);
        int raVien      = patients.Count(r => Date(r, "NgayXuat")  == reportDate);
        int tongGiuong  = bedCfg.Sum(r => Int(r, "TongGiuong"));
        double bor      = tongGiuong > 0 ? dangDieuTri * 100.0 / tongGiuong : 0;

        return new KpiGridSection("summary", "Tổng quan", [
            new("Đang điều trị",    dangDieuTri, "bệnh nhân", "number"),
            new("BOR",              bor,          "%",         "percent"),
            new("Vào viện hôm nay", vaoVien,      "lượt",      "number"),
            new("Ra viện hôm nay",  raVien,       "lượt",      "number"),
            new("ALOS",             CalcAlos(...), "ngày",     "days"),
        ]);
    }
}
```

---

## Phần 4 — Cách thêm dashboard mới từ A đến Z

Ví dụ: thêm **M03 — Phẫu thuật**.

### Bước 1: Xác định dữ liệu nguồn

Hỏi: *"HIS gửi dữ liệu phẫu thuật với tên field gì?"*

Giả sử HIS gửi:
```json
{
  "ma_bn": "BN001",
  "ten_pt": "Mổ ruột thừa",
  "ngay_pt": "2026-05-28",
  "bac_si": "BS Nguyễn",
  "loai_pt": "Nội soi",
  "ket_qua": "Thành công"
}
```

### Bước 2: Đăng ký SourceProfile (1 lần)

```http
POST /dm/sources
Content-Type: application/json

{
  "sourceSystem":    "his-01",
  "recordType":      "phau-thuat",
  "displayName":     "HIS - Phẫu thuật",
  "businessKeyField":"MRN",
  "mappings": {
    "ma_bn":   "MRN",
    "ten_pt":  "TenPhauThuat",
    "ngay_pt": "NgayPhauThuat",
    "bac_si":  "BacSiPhauThuat",
    "loai_pt": "LoaiPhauThuat",
    "ket_qua": "KetQua"
  }
}
```

> `mappings` = **tên field HIS** → **tên canonical** (field bạn dùng trong config)

### Bước 3: Tạo file config

Tạo file:
`DataMatchingService.Application/Dashboard/Configs/M03DashboardConfig.cs`

```csharp
using System.Text.Json;

namespace Hdos.DataMatchingService.Application.Dashboard.Configs;

public sealed class M03DashboardConfig : DashboardConfig
{
    public override string Code  => "m03";
    public override string Title => "Báo cáo Phẫu thuật";

    // Chỉ cần 1 record type
    public override IReadOnlyList<string> RecordTypes => ["phau-thuat"];

    public override List<DashboardSection> BuildSections(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate)
    {
        var rows = data.GetValueOrDefault("phau-thuat", []);

        return [
            BuildKpi(rows, reportDate),
            BuildLoaiPt(rows),
            BuildTable(rows),
        ];
    }

    private KpiGridSection BuildKpi(
        List<Dictionary<string, JsonElement>> rows,
        DateOnly reportDate)
    {
        int tongCa     = rows.Count;
        int homNay     = rows.Count(r => Date(r, "NgayPhauThuat") == reportDate);
        int thanhCong  = rows.Count(r => Str(r, "KetQua") == "Thành công");

        return new KpiGridSection("kpi", "Tổng quan phẫu thuật",
        [
            new("Tổng ca phẫu thuật",  tongCa,    "ca",  "number"),
            new("Ca hôm nay",          homNay,    "ca",  "number"),
            new("Thành công",          thanhCong, "ca",  "number"),
        ]);
    }

    private static PieChartSection BuildLoaiPt(List<Dictionary<string, JsonElement>> rows)
    {
        var groups = rows
            .GroupBy(r => Str(r, "LoaiPhauThuat") ?? "Khác")
            .Select(g => (Label: g.Key, Count: g.Count()))
            .ToList();

        int total = groups.Sum(g => g.Count);
        var slices = groups
            .OrderByDescending(g => g.Count)
            .Select(g => new PieSlice(
                g.Label, g.Count,
                total > 0 ? Math.Round(g.Count * 100.0 / total, 1) : 0))
            .ToList();

        return new PieChartSection("loai-pt", "Phân loại phẫu thuật", slices);
    }

    private static TableSection BuildTable(List<Dictionary<string, JsonElement>> rows)
    {
        List<TableColumn> columns =
        [
            new("mrn",          "Bệnh nhân",      "string"),
            new("tenPhauThuat", "Tên phẫu thuật", "string"),
            new("ngayPt",       "Ngày phẫu thuật","date"),
            new("bacSi",        "Bác sĩ",         "string"),
            new("loaiPt",       "Loại",           "badge"),
            new("ketQua",       "Kết quả",        "badge"),
        ];

        var tableRows = rows
            .Select(r => new Dictionary<string, object?>
            {
                ["mrn"]          = Str(r, "MRN"),
                ["tenPhauThuat"] = Str(r, "TenPhauThuat"),
                ["ngayPt"]       = Str(r, "NgayPhauThuat"),
                ["bacSi"]        = Str(r, "BacSiPhauThuat"),
                ["loaiPt"]       = Str(r, "LoaiPhauThuat"),
                ["ketQua"]       = Str(r, "KetQua"),
            })
            .ToList();

        return new TableSection("danh-sach", "Danh sách ca phẫu thuật", columns, tableRows);
    }
}
```

### Bước 4: Đăng ký vào DI (1 dòng)

File: `DataMatchingService.Application/DependencyInjection.cs`

```csharp
services.AddSingleton<DashboardConfig, M02DashboardConfig>();
services.AddSingleton<DashboardConfig, M03DashboardConfig>(); // ← thêm dòng này
services.AddSingleton<DashboardEngine>();
```

### Bước 5: Gọi API

```http
GET /dm/dashboards/m03?sourceSystem=his-01&date=2026-05-28
```

**Done.** Không sửa Engine, không sửa Controller, không sửa Frontend.

---

## Phần 5 — Response JSON đầy đủ

```http
GET /dm/dashboards/m02?sourceSystem=his-01&date=2026-05-28
```

```jsonc
{
  "success": true,
  "data": {
    "reportCode":  "m02",
    "reportTitle": "Trực quan Nội trú",
    "reportDate":  "2026-05-28",
    "generatedAt": "2026-05-28T09:14:00Z",
    "sections": [

      // Section 1: KPI Grid
      {
        "type":  "kpi-grid",
        "id":    "summary",
        "title": "Tổng quan",
        "items": [
          { "label": "Đang điều trị",    "value": 13, "unit": "bệnh nhân", "format": "number"  },
          { "label": "Tổng giường",      "value": 145,"unit": "giường",    "format": "number"  },
          { "label": "BOR",              "value": 9.0,"unit": "%",         "format": "percent" },
          { "label": "Vào viện hôm nay", "value": 4,  "unit": "lượt",     "format": "number"  },
          { "label": "Ra viện hôm nay",  "value": 2,  "unit": "lượt",     "format": "number"  },
          { "label": "ALOS",             "value": 4.6,"unit": "ngày/lượt", "format": "days"    }
        ]
      },

      // Section 2: Pie Chart
      {
        "type":  "pie-chart",
        "id":    "doi-tuong-kcb",
        "title": "Phân loại đối tượng KCB",
        "data": [
          { "label": "BHYT", "soLuong": 9, "phanTram": 75.0 },
          { "label": "DV",   "soLuong": 3, "phanTram": 25.0 }
        ]
      },

      // Section 3: Bar Chart
      {
        "type":  "bar-chart",
        "id":    "top-icd",
        "title": "Top 10 ICD hôm nay",
        "data": [
          { "label": "Viêm phổi, không xác định", "soLuong": 3 },
          { "label": "Sepsis",                    "soLuong": 2 },
          { "label": "STEMI",                     "soLuong": 1 }
        ]
      },

      // Section 4: Table
      {
        "type":  "table",
        "id":    "danh-sach-benh-nhan",
        "title": "Danh sách bệnh nhân nội trú",
        "columns": [
          { "key": "mrn",       "label": "Bệnh nhân / MRN", "type": "string" },
          { "key": "tenKhoa",   "label": "Khoa / Giường",   "type": "string" },
          { "key": "ngayNhap",  "label": "Ngày nhập",        "type": "date"   },
          { "key": "ngayXuat",  "label": "Ngày xuất",        "type": "date"   },
          { "key": "doiTuong",  "label": "Đối tượng",        "type": "badge"  },
          { "key": "trangThai", "label": "Trạng thái",       "type": "badge"  },
          { "key": "chanDoan",  "label": "Chẩn đoán",        "type": "string" }
        ],
        "rows": [
          {
            "mrn": "BN26000001", "tenBenhNhan": "Nguyễn Văn An",
            "tenKhoa": "Nội tổng hợp", "soGiuong": "NTH-01",
            "ngayNhap": "2026-05-24",  "ngayXuat": null,
            "doiTuong": "BHYT", "trangThai": "DangNoiTru", "chanDoan": "Viêm phổi"
          }
        ]
      }

    ]
  }
}
```

---

## Phần 6 — Frontend sử dụng như nào

Frontend chỉ cần gọi 1 endpoint và render theo `type`:

```typescript
// types.ts — tự gen từ Swagger hoặc viết tay
type SectionType = 'kpi-grid' | 'pie-chart' | 'bar-chart' | 'table'

interface KpiItem    { label: string; value: number; unit?: string; format: string }
interface KpiGrid    { type: 'kpi-grid';  id: string; title: string; items: KpiItem[] }
interface PieChart   { type: 'pie-chart'; id: string; title: string; data: { label:string; soLuong:number; phanTram:number }[] }
interface BarChart   { type: 'bar-chart'; id: string; title: string; data: { label:string; soLuong:number }[] }
interface TableCol   { key: string; label: string; type: string }
interface Table      { type: 'table'; id: string; title: string; columns: TableCol[]; rows: Record<string,any>[] }

type DashboardSection = KpiGrid | PieChart | BarChart | Table
```

```tsx
// DashboardPage.tsx
export default function DashboardPage({ code }: { code: string }) {
  const { data } = useSWR(`/dm/dashboards/${code}?sourceSystem=his-01`)

  return (
    <div>
      <h1>{data?.reportTitle}</h1>
      {data?.sections.map(section => (
        <SectionRenderer key={section.id} section={section} />
      ))}
    </div>
  )
}

// SectionRenderer.tsx — viết 1 lần, dùng cho mọi dashboard
function SectionRenderer({ section }: { section: DashboardSection }) {
  switch (section.type) {
    case 'kpi-grid':  return <KpiGrid  {...section} />
    case 'pie-chart': return <PieChart {...section} />
    case 'bar-chart': return <BarChart {...section} />
    case 'table':     return <DataTable {...section} />
  }
}
```

Khi thêm M03, M04 — **frontend không cần sửa gì**, chỉ truyền `code="m03"` vào `DashboardPage`.

---

## Phần 7 — Quy tắc khi thêm dashboard mới

**Bắt buộc:**

- [ ] File config đặt trong `Dashboard/Configs/`
- [ ] `Code` là slug viết thường, không dấu (vd: `"m03"`, `"chi-phi-khoa"`)
- [ ] `RecordTypes` liệt kê đúng tên RecordType đã đăng ký trong SourceProfile
- [ ] Đăng ký `services.AddSingleton<DashboardConfig, YourConfig>()` trước `AddSingleton<DashboardEngine>()`

**Canonical field names:**

- Dùng PascalCase: `MRN`, `TenKhoa`, `NgayNhap`, `TongGiuong`
- Key trong `TableSection.Rows` dùng camelCase: `"mrn"`, `"tenKhoa"`, `"ngayNhap"`
- Lý do: canonical field là domain name (C# style), row key là JSON cho frontend (JS style)

**Nếu cần section type mới (vd: `line-chart`):**

1. Thêm class `LineChartSection` vào `DashboardSection.cs`
2. Thêm `[JsonDerivedType(typeof(LineChartSection), "line-chart")]`
3. Thêm component `<LineChart>` vào frontend
4. Dùng trong bất kỳ config nào

---

## Phần 8 — Checklist nhanh

```
Thêm dashboard mới:
  [1] POST /dm/sources          → đăng ký SourceProfile + mappings
  [2] POST /dm/ingest/json      → ingest dữ liệu mẫu để test
  [3] Tạo XxxDashboardConfig.cs → override Code, Title, RecordTypes, BuildSections
  [4] Thêm 1 dòng DI            → services.AddSingleton<DashboardConfig, XxxDashboardConfig>()
  [5] Test: GET /dm/dashboards/{code}
  [6] (Optional) Cập nhật docs này
```
