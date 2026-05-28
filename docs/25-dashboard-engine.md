# 25 — Dashboard Config Engine

Hệ thống tạo dashboard tự động từ config. Mỗi dashboard là **1 file config C#** — không cần viết handler, không cần viết aggregation logic, không cần sửa frontend.

---

## Kiến trúc tổng quan

```
DashboardConfig (abstract)          ← bạn kế thừa để tạo dashboard mới
       │
       │  override Code, Title, RecordTypes, BuildSections()
       ▼
DashboardEngine                     ← 1 class, dùng chung cho mọi dashboard
   1. Lookup config theo code
   2. Fetch StagingRecord (song song, theo RecordTypes)
   3. Parse CanonicalPayload → List<Row>
   4. Gọi config.BuildSections(data, reportDate)
   5. Trả về DashboardResult { sections[] }
       │
       ▼
GET /dm/dashboards/{code}           ← 1 endpoint, phục vụ mọi dashboard
       │
       ▼
Frontend <DashboardRenderer>        ← switch theo section.type, viết 1 lần
```

---

## Endpoint

| Method | URL | Mô tả |
|--------|-----|-------|
| `GET` | `/dm/dashboards` | Liệt kê tất cả dashboard đã đăng ký |
| `GET` | `/dm/dashboards/{code}` | Lấy dữ liệu dashboard |

**Query params của `GET /dm/dashboards/{code}`:**

| Param | Bắt buộc | Mô tả |
|---|---|---|
| `sourceSystem` | Không | Lọc theo nguồn HIS (vd: `his-01`). Bỏ = lấy tất cả |
| `date` | Không | Ngày báo cáo `yyyy-MM-dd`. Mặc định = hôm nay UTC |

---

## Cấu trúc Response

Mọi dashboard đều trả về cùng shape:

```jsonc
{
  "success": true,
  "data": {
    "reportCode":  "m02",
    "reportTitle": "Trực quan Nội trú",
    "reportDate":  "2026-05-28",
    "generatedAt": "2026-05-28T09:14:00Z",
    "sections": [
      { "type": "kpi-grid",  "id": "summary",            ... },
      { "type": "pie-chart", "id": "doi-tuong-kcb",      ... },
      { "type": "bar-chart", "id": "top-icd",            ... },
      { "type": "table",     "id": "danh-sach-benh-nhan",... }
    ]
  }
}
```

---

## Section Types

### `kpi-grid` — Các số KPI dạng card

```jsonc
{
  "type":  "kpi-grid",
  "id":    "summary",
  "title": "Tổng quan",
  "items": [
    { "label": "Đang điều trị", "value": 12,  "unit": "bệnh nhân", "format": "number"  },
    { "label": "BOR",           "value": 80.0,"unit": "%",         "format": "percent" },
    { "label": "ALOS",          "value": 4.2, "unit": "ngày/lượt", "format": "days"    }
  ]
}
```

**`format` hợp lệ:** `"number"` `"percent"` `"currency"` `"days"`

---

### `pie-chart` — Phân bổ tỷ lệ %

```jsonc
{
  "type":  "pie-chart",
  "id":    "doi-tuong-kcb",
  "title": "Phân loại đối tượng KCB",
  "data": [
    { "label": "BHYT", "soLuong": 9, "phanTram": 75.0 },
    { "label": "DV",   "soLuong": 2, "phanTram": 16.7 },
    { "label": "Khac", "soLuong": 1, "phanTram": 8.3  }
  ]
}
```

---

### `bar-chart` — So sánh theo nhóm

```jsonc
{
  "type":  "bar-chart",
  "id":    "top-icd",
  "title": "Top 10 ICD hôm nay",
  "data": [
    { "label": "Viêm phổi, không xác định", "soLuong": 3 },
    { "label": "Sepsis",                    "soLuong": 2 }
  ]
}
```

---

### `table` — Danh sách chi tiết

```jsonc
{
  "type":  "table",
  "id":    "danh-sach-benh-nhan",
  "title": "Danh sách bệnh nhân nội trú",
  "columns": [
    { "key": "mrn",       "label": "Bệnh nhân / MRN", "type": "string" },
    { "key": "tenKhoa",   "label": "Khoa / Giường",   "type": "string" },
    { "key": "ngayNhap",  "label": "Ngày nhập",        "type": "date"   },
    { "key": "doiTuong",  "label": "Đối tượng",        "type": "badge"  },
    { "key": "trangThai", "label": "Trạng thái",       "type": "badge"  }
  ],
  "rows": [
    {
      "mrn": "BN26000001", "tenBenhNhan": "Nguyễn Văn An",
      "tenKhoa": "Nội tổng hợp", "soGiuong": "NTH-01",
      "ngayNhap": "2026-05-24", "ngayXuat": null,
      "doiTuong": "BHYT", "trangThai": "DangNoiTru", "chanDoan": "Viêm phổi"
    }
  ]
}
```

**`column.type` hợp lệ:** `"string"` `"number"` `"currency"` `"date"` `"badge"`

---

## Cách thêm dashboard mới (ví dụ M03)

### Bước 1 — Tạo file config

```
DataMatchingService.Application/Dashboard/Configs/M03DashboardConfig.cs
```

```csharp
public sealed class M03DashboardConfig : DashboardConfig
{
    public override string Code  => "m03";
    public override string Title => "Báo cáo Phẫu thuật";

    public override IReadOnlyList<string> RecordTypes => ["phau-thuat"];

    public override List<DashboardSection> BuildSections(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate)
    {
        var rows = data.GetValueOrDefault("phau-thuat", []);

        return
        [
            new KpiGridSection("summary", "Tổng quan",
            [
                new("Tổng phẫu thuật", rows.Count, "ca", "number"),
                new("Phẫu thuật hôm nay",
                    rows.Count(r => Date(r, "NgayPhauThuat") == reportDate),
                    "ca", "number"),
            ]),

            new PieChartSection("loai-pt", "Phân loại phẫu thuật",
                rows.GroupBy(r => Str(r, "LoaiPhauThuat") ?? "Khác")
                    .Select(g => (Label: g.Key, Count: g.Count()))
                    .Let(groups =>
                    {
                        int total = groups.Sum(g => g.Count);
                        return groups
                            .OrderByDescending(g => g.Count)
                            .Select(g => new PieSlice(g.Label, g.Count,
                                total > 0 ? Math.Round(g.Count * 100.0 / total, 1) : 0))
                            .ToList();
                    })),

            new TableSection("danh-sach", "Danh sách ca phẫu thuật",
            [
                new("mrn",          "Bệnh nhân",     "string"),
                new("tenPhauThuat", "Tên phẫu thuật","string"),
                new("ngayPt",       "Ngày PT",       "date"),
                new("bacSiPt",      "Bác sĩ PT",     "string"),
                new("ketQua",       "Kết quả",       "badge"),
            ],
            rows.Select(r => new Dictionary<string, object?>
            {
                ["mrn"]          = Str(r, "MRN"),
                ["tenPhauThuat"] = Str(r, "TenPhauThuat"),
                ["ngayPt"]       = Str(r, "NgayPhauThuat"),
                ["bacSiPt"]      = Str(r, "BacSiPhauThuat"),
                ["ketQua"]       = Str(r, "KetQua"),
            }).ToList()),
        ];
    }
}
```

> **Lưu ý:** `Let()` là extension tự viết nếu cần, hoặc dùng biến trung gian thay thế.

### Bước 2 — Đăng ký trong DI (1 dòng)

File: `DataMatchingService.Application/DependencyInjection.cs`

```csharp
services.AddSingleton<DashboardConfig, M02DashboardConfig>();
services.AddSingleton<DashboardConfig, M03DashboardConfig>(); // ← thêm dòng này
services.AddSingleton<DashboardEngine>();
```

### Bước 3 — Đăng ký SourceProfile cho RecordType mới

```http
POST /dm/sources
{
  "sourceSystem": "his-01",
  "recordType": "phau-thuat",
  "displayName": "HIS - Phẫu thuật",
  "businessKeyField": "MRN",
  "mappings": {
    "ma_bn":       "MRN",
    "ten_pt":      "TenPhauThuat",
    "ngay_pt":     "NgayPhauThuat",
    "bac_si_pt":   "BacSiPhauThuat",
    "loai_pt":     "LoaiPhauThuat",
    "ket_qua":     "KetQua"
  }
}
```

### Bước 4 — Done

```http
GET /dm/dashboards/m03?sourceSystem=his-01&date=2026-05-28
```

**Không cần sửa gì ở frontend** — endpoint trả về `sections[]` cùng format.

---

## Danh sách dashboard hiện có

```http
GET /dm/dashboards
→ ["m02"]
```

---

## Cấu trúc file trong codebase

```
DataMatchingService.Application/
  Dashboard/
    DashboardSection.cs          ← base + KpiGridSection, PieChartSection, BarChartSection, TableSection
    DashboardConfig.cs           ← abstract base (override để tạo dashboard mới)
    DashboardEngine.cs           ← engine fetch + aggregate + trả sections[]
    Configs/
      M02DashboardConfig.cs      ← M02 Trực quan Nội trú
      M03DashboardConfig.cs      ← (ví dụ, chưa có)
DataMatchingService.API/
  Controllers/
    DashboardsController.cs      ← GET /dm/dashboards, GET /dm/dashboards/{code}
```

---

## Frontend chỉ cần viết 1 lần

```tsx
// <DashboardRenderer sections={sections} />
function DashboardRenderer({ sections }) {
  return sections.map(section => {
    switch (section.type) {
      case 'kpi-grid':  return <KpiGrid   key={section.id} {...section} />
      case 'pie-chart': return <PieChart  key={section.id} {...section} />
      case 'bar-chart': return <BarChart  key={section.id} {...section} />
      case 'table':     return <DataTable key={section.id} {...section} />
    }
  })
}
```

Thêm dashboard M03, M04, M05... → **0 dòng frontend mới**.
Thêm section type mới (vd `line-chart`) → thêm 1 lần vào engine + 1 component frontend.

---

## Test nhanh với M02

### 1. Đăng ký SourceProfile

```http
POST http://localhost:5004/dm/sources
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "recordType": "benh-nhan-noi-tru",
  "displayName": "HIS - Bệnh nhân nội trú",
  "businessKeyField": "MRN",
  "mappings": {
    "ma_bn": "MRN", "ho_ten": "TenBenhNhan", "ten_khoa": "TenKhoa",
    "so_giuong": "SoGiuong", "ngay_nhap": "NgayNhap", "ngay_xuat": "NgayXuat",
    "doi_tuong": "DoiTuong", "trang_thai": "TrangThai",
    "ma_icd": "MaICD", "ten_icd": "TenICD", "chan_doan": "ChanDoan"
  }
}
```

### 2. Ingest dữ liệu mẫu (xem docs/24-m02-noi-tru-dashboard.md)

### 3. Gọi dashboard

```http
GET http://localhost:5004/dm/dashboards/m02?sourceSystem=his-01&date=2026-05-28
```

### 4. Xem danh sách dashboard đã đăng ký

```http
GET http://localhost:5004/dm/dashboards
→ { "success": true, "data": ["m02"] }
```
