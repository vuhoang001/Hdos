# 24 — Dashboard Engine & DataMatchingService

Hướng dẫn đầy đủ: kiến trúc, luồng dữ liệu từ HIS vào đến dashboard,
cách thêm dashboard mới, và cách test.

---

## 1. Luồng dữ liệu tổng quan

```
[HIS / bên thứ 3]
    │
    ├─ POST /dm/sources          (1 lần — đăng ký ánh xạ field HIS → canonical)
    │
    ├─ POST /dm/ingest/json      (1 record/lần — gọi từ HIS theo event)
    └─ POST /dm/ingest/file      (nhiều record/lần — upload file JSON hoặc CSV)
    │
    ▼
[StagingRecord — PostgreSQL]
    SourceSystem     = "his-01"
    RecordType       = "benh-nhan-noi-tru"
    RawPayload       = JSON gốc từ HIS
    CanonicalPayload = JSON chuẩn hóa (sau khi apply mappings)
    Status           = Pending → Matched (xử lý bởi MatchingWorker nền)
    │
    ▼
[GET /dm/dashboards/{code}]
    DashboardEngine tìm config theo code
    → Fetch StagingRecord (Status=Matched) song song theo RecordTypes
    → Parse CanonicalPayload → gọi config.BuildSections()
    → Trả sections[]
    │
    ▼
[Frontend Next.js]
    <DashboardRenderer sections={sections} />
    switch(type) → render đúng component
```

---

## 2. API Endpoints

### Quản lý nguồn dữ liệu

| Method | URL | Mô tả |
|--------|-----|-------|
| `POST` | `/dm/sources` | Đăng ký SourceProfile (ánh xạ field) |
| `GET`  | `/dm/sources` | Liệt kê SourceProfile đã đăng ký |

### Ingest dữ liệu

| Method | URL | Mô tả |
|--------|-----|-------|
| `POST` | `/dm/ingest/json` | Ingest **1 record** dạng JSON |
| `POST` | `/dm/ingest/file` | Ingest **nhiều record** qua file `.json` hoặc `.csv` |

### Dashboard

| Method | URL | Mô tả |
|--------|-----|-------|
| `GET`  | `/dm/dashboards` | Liệt kê dashboard đã đăng ký |
| `GET`  | `/dm/dashboards/{code}` | Lấy dữ liệu dashboard |

**Query params của `GET /dm/dashboards/{code}`:**

| Param | Bắt buộc | Mô tả |
|---|---|---|
| `sourceSystem` | Không | Lọc theo nguồn (vd: `his-01`). Bỏ = lấy tất cả |
| `date` | Không | Ngày báo cáo `yyyy-MM-dd`. Mặc định = hôm nay UTC |

---

## 3. Ingest dữ liệu — Cách dùng đúng

### 3.1 Ingest 1 record (`/dm/ingest/json`)

`payload` là **1 object đơn** — đúng với tên field của HIS (trước khi mapping):

```http
POST /dm/ingest/json
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "recordType":   "benh-nhan-noi-tru",
  "payload": {
    "ma_bn":      "BN26000001",
    "ho_ten":     "Nguyễn Văn An",
    "ten_khoa":   "Nội tổng hợp",
    "so_giuong":  "NTH-01",
    "ngay_nhap":  "2026-05-24",
    "ngay_xuat":  null,
    "doi_tuong":  "BHYT",
    "trang_thai": "DangNoiTru",
    "ma_icd":     "J18.9",
    "ten_icd":    "Viêm phổi, không xác định",
    "chan_doan":  "Viêm phổi cấp"
  }
}
```

Response `202 Accepted`:
```json
{ "success": true, "data": { "id": "...", "status": "Pending" } }
```

> Record bắt đầu ở `Status=Pending`. MatchingWorker chạy nền mỗi 30 giây, chuyển sang `Status=Matched`. Dashboard chỉ đọc record có `Status=Matched`.

### 3.2 Ingest nhiều record (`/dm/ingest/file`)

Upload file JSON array hoặc CSV — phù hợp để test với nhiều dữ liệu cùng lúc.

**File `benh-nhan.json`** (mảng các object HIS):

```json
[
  {
    "ma_bn": "BN26000001", "ho_ten": "Nguyễn Văn An",
    "ten_khoa": "Nội tổng hợp", "so_giuong": "NTH-01",
    "ngay_nhap": "2026-05-24", "ngay_xuat": null,
    "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
    "ma_icd": "J18.9", "ten_icd": "Viêm phổi, không xác định", "chan_doan": "Viêm phổi"
  },
  {
    "ma_bn": "BN26000002", "ho_ten": "Trần Thị Bình",
    "ten_khoa": "Nội tổng hợp", "so_giuong": "NTH-02",
    "ngay_nhap": "2026-05-22", "ngay_xuat": null,
    "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
    "ma_icd": "A41.9", "ten_icd": "Nhiễm khuẩn huyết", "chan_doan": "Sepsis"
  },
  {
    "ma_bn": "BN26000003", "ho_ten": "Lê Minh Cường",
    "ten_khoa": "Ngoại khoa", "so_giuong": "NG-08",
    "ngay_nhap": "2026-05-26", "ngay_xuat": null,
    "doi_tuong": "DV", "trang_thai": "DangNoiTru",
    "ma_icd": "K35.2", "ten_icd": "Viêm ruột thừa cấp", "chan_doan": "Appendicitis"
  },
  {
    "ma_bn": "BN26000004", "ho_ten": "Phạm Thị Dung",
    "ten_khoa": "Sản khoa", "so_giuong": "SAN-04",
    "ngay_nhap": "2026-05-25", "ngay_xuat": "2026-05-28",
    "doi_tuong": "BHYT", "trang_thai": "DaXuatVien",
    "ma_icd": "Z39.0", "ten_icd": "Hậu sản bình thường", "chan_doan": "Sau sinh thường"
  },
  {
    "ma_bn": "BN26000005", "ho_ten": "Hoàng Văn Đức",
    "ten_khoa": "ICU", "so_giuong": "ICU-01",
    "ngay_nhap": "2026-05-20", "ngay_xuat": null,
    "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
    "ma_icd": "I21.0", "ten_icd": "NMCT cấp ST chênh lên (STEMI)", "chan_doan": "STEMI"
  },
  {
    "ma_bn": "BN26000006", "ho_ten": "Vũ Thị Hoa",
    "ten_khoa": "ICU", "so_giuong": "ICU-02",
    "ngay_nhap": "2026-05-21", "ngay_xuat": null,
    "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
    "ma_icd": "J80", "ten_icd": "Hội chứng suy hô hấp cấp (ARDS)", "chan_doan": "ARDS"
  },
  {
    "ma_bn": "BN26000007", "ho_ten": "Đặng Minh Tuấn",
    "ten_khoa": "Nội tổng hợp", "so_giuong": "NTH-05",
    "ngay_nhap": "2026-05-28", "ngay_xuat": null,
    "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
    "ma_icd": "J18.9", "ten_icd": "Viêm phổi, không xác định", "chan_doan": "Viêm phổi cấp"
  },
  {
    "ma_bn": "BN26000008", "ho_ten": "Ngô Thị Lan",
    "ten_khoa": "Nhi khoa", "so_giuong": "NHI-03",
    "ngay_nhap": "2026-05-27", "ngay_xuat": null,
    "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
    "ma_icd": "J18.9", "ten_icd": "Viêm phổi, không xác định", "chan_doan": "Viêm phổi trẻ em"
  },
  {
    "ma_bn": "BN26000009", "ho_ten": "Bùi Văn Hải",
    "ten_khoa": "Ngoại khoa", "so_giuong": "NG-12",
    "ngay_nhap": "2026-05-28", "ngay_xuat": null,
    "doi_tuong": "DV", "trang_thai": "DangNoiTru",
    "ma_icd": "S72.0", "ten_icd": "Gãy cổ xương đùi", "chan_doan": "Gãy cổ xương đùi phải"
  },
  {
    "ma_bn": "BN26000010", "ho_ten": "Đinh Thị Mai",
    "ten_khoa": "Sản khoa", "so_giuong": "SAN-07",
    "ngay_nhap": "2026-05-28", "ngay_xuat": null,
    "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
    "ma_icd": "O20.0", "ten_icd": "Dọa sảy thai", "chan_doan": "Dọa sảy thai 12 tuần"
  },
  {
    "ma_bn": "BN26000011", "ho_ten": "Trịnh Văn Nam",
    "ten_khoa": "ICU", "so_giuong": "ICU-03",
    "ngay_nhap": "2026-05-19", "ngay_xuat": null,
    "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
    "ma_icd": "I63.5", "ten_icd": "Nhồi máu não do tắc ĐM não", "chan_doan": "Đột quỵ nhồi máu não"
  },
  {
    "ma_bn": "BN26000012", "ho_ten": "Lý Thị Phương",
    "ten_khoa": "Nội tổng hợp", "so_giuong": "NTH-08",
    "ngay_nhap": "2026-05-23", "ngay_xuat": null,
    "doi_tuong": "Khac", "trang_thai": "DangNoiTru",
    "ma_icd": "E11.9", "ten_icd": "Đái tháo đường type 2", "chan_doan": "ĐTĐ type 2"
  },
  {
    "ma_bn": "BN26000013", "ho_ten": "Phan Văn Khánh",
    "ten_khoa": "Ngoại khoa", "so_giuong": "NG-15",
    "ngay_nhap": "2026-05-26", "ngay_xuat": null,
    "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
    "ma_icd": "C34.1", "ten_icd": "Ung thư phổi thuỳ trên", "chan_doan": "UTPQ thuỳ trên phổi trái"
  },
  {
    "ma_bn": "BN26000014", "ho_ten": "Cao Thị Xuân",
    "ten_khoa": "Nhi khoa", "so_giuong": "NHI-06",
    "ngay_nhap": "2026-05-27", "ngay_xuat": "2026-05-28",
    "doi_tuong": "BHYT", "trang_thai": "DaXuatVien",
    "ma_icd": "A09", "ten_icd": "Tiêu chảy cấp", "chan_doan": "Tiêu chảy cấp mất nước"
  },
  {
    "ma_bn": "BN26000015", "ho_ten": "Dương Minh Khoa",
    "ten_khoa": "ICU", "so_giuong": "ICU-05",
    "ngay_nhap": "2026-05-28", "ngay_xuat": null,
    "doi_tuong": "DV", "trang_thai": "DangNoiTru",
    "ma_icd": "B08.4", "ten_icd": "Bệnh tay chân miệng", "chan_doan": "Tay chân miệng độ 3"
  }
]
```

**Gọi API upload:**

```http
POST /dm/ingest/file
Content-Type: multipart/form-data

sourceSystem: his-01
recordType:   benh-nhan-noi-tru
file:         benh-nhan.json
```

Response `202 Accepted`:
```json
{ "success": true, "data": { "count": 15, "ids": ["...", "..."] } }
```

---

## 4. Test M02 từ đầu đến cuối

### Bước 1 — Đăng ký SourceProfile bệnh nhân nội trú

```http
POST http://localhost:5004/dm/sources
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "recordType":   "benh-nhan-noi-tru",
  "displayName":  "HIS - Bệnh nhân nội trú",
  "businessKeyField": "MRN",
  "mappings": {
    "ma_bn":      "MRN",
    "ho_ten":     "TenBenhNhan",
    "ten_khoa":   "TenKhoa",
    "so_giuong":  "SoGiuong",
    "ngay_nhap":  "NgayNhap",
    "ngay_xuat":  "NgayXuat",
    "doi_tuong":  "DoiTuong",
    "trang_thai": "TrangThai",
    "ma_icd":     "MaICD",
    "ten_icd":    "TenICD",
    "chan_doan":   "ChanDoan"
  }
}
```

### Bước 2 — Đăng ký SourceProfile cấu hình giường (để tính BOR%)

```http
POST http://localhost:5004/dm/sources
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "recordType":   "cau-hinh-giuong",
  "displayName":  "HIS - Cấu hình giường",
  "businessKeyField": "TenKhoa",
  "mappings": {
    "ten_khoa":    "TenKhoa",
    "tong_giuong": "TongGiuong"
  }
}
```

### Bước 3 — Ingest cấu hình giường (file upload)

Tạo file `giuong.json`:
```json
[
  { "ten_khoa": "Nội tổng hợp", "tong_giuong": 40 },
  { "ten_khoa": "ICU",          "tong_giuong": 15 },
  { "ten_khoa": "Ngoại khoa",   "tong_giuong": 35 },
  { "ten_khoa": "Sản khoa",     "tong_giuong": 30 },
  { "ten_khoa": "Nhi khoa",     "tong_giuong": 25 }
]
```

```http
POST http://localhost:5004/dm/ingest/file
Content-Type: multipart/form-data

sourceSystem: his-01
recordType:   cau-hinh-giuong
file:         giuong.json
```

### Bước 4 — Ingest 15 bệnh nhân mẫu (file upload)

Lưu nội dung mảng JSON ở mục 3.2 thành file `benh-nhan.json`, rồi:

```http
POST http://localhost:5004/dm/ingest/file
Content-Type: multipart/form-data

sourceSystem: his-01
recordType:   benh-nhan-noi-tru
file:         benh-nhan.json
```

### Bước 5 — Chờ MatchingWorker xử lý

MatchingWorker chạy nền mỗi 30 giây, tự động chuyển record từ `Pending` → `Matched`.
Không cần làm gì thêm. Kiểm tra trạng thái nếu cần:

```http
GET http://localhost:5004/dm/records?sourceSystem=his-01&recordType=benh-nhan-noi-tru
```

### Bước 6 — Gọi dashboard

```http
GET http://localhost:5004/dm/dashboards/m02?sourceSystem=his-01&date=2026-05-28
```

**Kết quả mong đợi với 15 bệnh nhân mẫu (date = 2026-05-28):**

| Metric | Giá trị | Lý do |
|--------|---------|-------|
| `dangDieuTri` | 13 | 15 - 2 đã xuất (BN004, BN014) |
| `vaoVienHomNay` | 4 | BN007, BN009, BN010, BN015 nhập 2026-05-28 |
| `raVienHomNay` | 2 | BN004, BN014 xuất 2026-05-28 |
| Top ICD | J18.9 (3 ca) | BN001, BN007, BN008 |
| `borPercent` | ~11% | 13 / 115 giường tổng |

---

## 5. Cấu trúc Response Dashboard

Mọi dashboard đều trả về cùng 1 shape:

```jsonc
{
  "success": true,
  "data": {
    "reportCode":  "m02",
    "reportTitle": "Trực quan Nội trú",
    "reportDate":  "2026-05-28",
    "generatedAt": "2026-05-28T09:14:00Z",
    "sections": [
      {
        "type": "kpi-grid", "id": "summary", "title": "Tổng quan",
        "items": [
          { "label": "Đang điều trị",    "value": 13, "unit": "bệnh nhân", "format": "number"  },
          { "label": "Tổng giường",      "value": 115,"unit": "giường",    "format": "number"  },
          { "label": "BOR",              "value": 11.3,"unit": "%",        "format": "percent" },
          { "label": "Vào viện hôm nay", "value": 4,  "unit": "lượt",     "format": "number"  },
          { "label": "Ra viện hôm nay",  "value": 2,  "unit": "lượt",     "format": "number"  },
          { "label": "ALOS",             "value": 5.1,"unit": "ngày/lượt", "format": "days"    }
        ]
      },
      {
        "type": "pie-chart", "id": "doi-tuong-kcb", "title": "Phân loại đối tượng KCB",
        "data": [
          { "label": "BHYT", "soLuong": 10, "phanTram": 76.9 },
          { "label": "DV",   "soLuong": 2,  "phanTram": 15.4 },
          { "label": "Khac", "soLuong": 1,  "phanTram": 7.7  }
        ]
      },
      {
        "type": "bar-chart", "id": "top-icd", "title": "Top 10 ICD hôm nay",
        "data": [
          { "label": "Viêm phổi, không xác định", "soLuong": 3 },
          { "label": "Sepsis",                    "soLuong": 1 }
        ]
      },
      {
        "type": "table", "id": "danh-sach-benh-nhan", "title": "Danh sách bệnh nhân nội trú",
        "columns": [
          { "key": "mrn",       "label": "MRN",        "type": "string" },
          { "key": "tenKhoa",   "label": "Khoa",       "type": "string" },
          { "key": "ngayNhap",  "label": "Ngày nhập",  "type": "date"   },
          { "key": "ngayXuat",  "label": "Ngày xuất",  "type": "date"   },
          { "key": "doiTuong",  "label": "Đối tượng",  "type": "badge"  },
          { "key": "trangThai", "label": "Trạng thái", "type": "badge"  },
          { "key": "chanDoan",  "label": "Chẩn đoán",  "type": "string" }
        ],
        "rows": [ { "mrn": "BN26000001", "tenBenhNhan": "Nguyễn Văn An", "..." : "..." } ]
      }
    ]
  }
}
```

### Section types hiện có

| `type` | Render | Fields |
|--------|--------|--------|
| `kpi-grid` | Card số liệu | `items[]{label, value, unit, format}` |
| `pie-chart` | Biểu đồ tròn | `data[]{label, soLuong, phanTram}` |
| `bar-chart` | Biểu đồ cột | `data[]{label, soLuong}` |
| `table` | Bảng | `columns[], rows[]` |

**`format` của KPI:** `"number"` `"percent"` `"currency"` `"days"`

**`type` của cột table:** `"string"` `"number"` `"currency"` `"date"` `"badge"`

---

## 6. Thêm dashboard mới (ví dụ M03 Phẫu thuật)

### Bước 1 — Đăng ký SourceProfile

```http
POST /dm/sources
Content-Type: application/json

{
  "sourceSystem":     "his-01",
  "recordType":       "phau-thuat",
  "displayName":      "HIS - Phẫu thuật",
  "businessKeyField": "MRN",
  "mappings": {
    "ma_bn":    "MRN",
    "ten_pt":   "TenPhauThuat",
    "ngay_pt":  "NgayPhauThuat",
    "bac_si":   "BacSiPhauThuat",
    "loai_pt":  "LoaiPhauThuat",
    "ket_qua":  "KetQua"
  }
}
```

### Bước 2 — Tạo file config

`DataMatchingService.Application/Dashboard/Configs/M03DashboardConfig.cs`

```csharp
using System.Text.Json;

namespace Hdos.DataMatchingService.Application.Dashboard.Configs;

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
        return [BuildKpi(rows, reportDate), BuildLoaiPt(rows), BuildTable(rows)];
    }

    private KpiGridSection BuildKpi(List<Dictionary<string, JsonElement>> rows, DateOnly reportDate) =>
        new("kpi", "Tổng quan phẫu thuật",
        [
            new("Tổng ca",    rows.Count,                                              "ca", "number"),
            new("Hôm nay",    rows.Count(r => Date(r, "NgayPhauThuat") == reportDate), "ca", "number"),
            new("Thành công", rows.Count(r => Str(r, "KetQua") == "Thành công"),       "ca", "number"),
        ]);

    private static PieChartSection BuildLoaiPt(List<Dictionary<string, JsonElement>> rows)
    {
        var groups = rows.GroupBy(r => Str(r, "LoaiPhauThuat") ?? "Khác")
                         .Select(g => (Label: g.Key, Count: g.Count())).ToList();
        int total = groups.Sum(g => g.Count);
        return new("loai-pt", "Phân loại phẫu thuật",
            groups.OrderByDescending(g => g.Count)
                  .Select(g => new PieSlice(g.Label, g.Count,
                      total > 0 ? Math.Round(g.Count * 100.0 / total, 1) : 0))
                  .ToList());
    }

    private static TableSection BuildTable(List<Dictionary<string, JsonElement>> rows) =>
        new("danh-sach", "Danh sách ca phẫu thuật",
        [
            new("mrn",          "Bệnh nhân",      "string"),
            new("tenPhauThuat", "Tên phẫu thuật", "string"),
            new("ngayPt",       "Ngày PT",        "date"),
            new("bacSi",        "Bác sĩ",         "string"),
            new("loaiPt",       "Loại",           "badge"),
            new("ketQua",       "Kết quả",        "badge"),
        ],
        rows.Select(r => new Dictionary<string, object?>
        {
            ["mrn"]          = Str(r, "MRN"),
            ["tenPhauThuat"] = Str(r, "TenPhauThuat"),
            ["ngayPt"]       = Str(r, "NgayPhauThuat"),
            ["bacSi"]        = Str(r, "BacSiPhauThuat"),
            ["loaiPt"]       = Str(r, "LoaiPhauThuat"),
            ["ketQua"]       = Str(r, "KetQua"),
        }).ToList());
}
```

### Bước 3 — Đăng ký DI (1 dòng)

`DataMatchingService.Application/DependencyInjection.cs`:

```csharp
services.AddSingleton<DashboardConfig, M02DashboardConfig>();
services.AddSingleton<DashboardConfig, M03DashboardConfig>(); // ← thêm dòng này
services.AddSingleton<DashboardEngine>();
```

### Bước 4 — Gọi API

```http
GET /dm/dashboards/m03?sourceSystem=his-01&date=2026-05-28
```

**Không cần sửa gì ở Controller hay Frontend.**

---

## 7. Cấu trúc code trong codebase

```
Dashboard/
  DashboardSection.cs   ← abstract base + KpiGridSection, PieChartSection,
                           BarChartSection, TableSection (JsonPolymorphic)
  DashboardConfig.cs    ← abstract base với helpers Str/Int/Dec/Date
  DashboardEngine.cs    ← fetch song song, parse JSON, gọi BuildSections()
  Configs/
    M02DashboardConfig.cs
```

### Canonical fields của M02

**RecordType `benh-nhan-noi-tru`:**

| Canonical field | HIS field | Bắt buộc | Giá trị mẫu |
|-----------------|-----------|----------|-------------|
| `MRN`           | `ma_bn`      | Có | `"BN26000001"` |
| `TenBenhNhan`   | `ho_ten`     | Có | `"Nguyễn Văn An"` |
| `TenKhoa`       | `ten_khoa`   | Có | `"Nội tổng hợp"` |
| `SoGiuong`      | `so_giuong`  | Không | `"NTH-01"` |
| `NgayNhap`      | `ngay_nhap`  | Có | `"2026-05-24"` |
| `NgayXuat`      | `ngay_xuat`  | Không | `null` hoặc `"2026-05-28"` |
| `DoiTuong`      | `doi_tuong`  | Có | `"BHYT"` / `"DV"` / `"Khac"` |
| `TrangThai`     | `trang_thai` | Có | `"DangNoiTru"` / `"DaXuatVien"` |
| `MaICD`         | `ma_icd`     | Không | `"J18.9"` |
| `TenICD`        | `ten_icd`    | Không | `"Viêm phổi"` |
| `ChanDoan`      | `chan_doan`  | Không | `"Viêm phổi cấp"` |

**RecordType `cau-hinh-giuong`** (để tính BOR%):

| Canonical field | HIS field | Giá trị mẫu |
|-----------------|-----------|-------------|
| `TenKhoa`       | `ten_khoa`    | `"ICU"` |
| `TongGiuong`    | `tong_giuong` | `15` |

---

## 8. Frontend — viết 1 lần, dùng mọi dashboard

```typescript
// types.ts
interface KpiItem   { label: string; value: number; unit?: string; format: string }
interface KpiGrid   { type: 'kpi-grid';  id: string; title: string; items: KpiItem[] }
interface PieChart  { type: 'pie-chart'; id: string; title: string; data: { label: string; soLuong: number; phanTram: number }[] }
interface BarChart  { type: 'bar-chart'; id: string; title: string; data: { label: string; soLuong: number }[] }
interface TableCol  { key: string; label: string; type: string }
interface DataTable { type: 'table'; id: string; title: string; columns: TableCol[]; rows: Record<string, unknown>[] }
type DashboardSection = KpiGrid | PieChart | BarChart | DataTable
```

```tsx
// DashboardPage.tsx
export default function DashboardPage({ code }: { code: string }) {
  const { data } = useSWR(`/dm/dashboards/${code}?sourceSystem=his-01`)
  return (
    <>
      <h1>{data?.reportTitle}</h1>
      {data?.sections.map(s => <SectionRenderer key={s.id} section={s} />)}
    </>
  )
}

function SectionRenderer({ section }: { section: DashboardSection }) {
  switch (section.type) {
    case 'kpi-grid':  return <KpiGrid   {...section} />
    case 'pie-chart': return <PieChart  {...section} />
    case 'bar-chart': return <BarChart  {...section} />
    case 'table':     return <DataTable {...section} />
  }
}
```

---

## 9. Checklist thêm dashboard mới

```
[1] POST /dm/sources          — đăng ký SourceProfile + mappings (1 lần)
[2] POST /dm/ingest/file      — upload file JSON array để test
[3] Tạo XxxDashboardConfig.cs — override Code, Title, RecordTypes, BuildSections()
[4] +1 dòng DI                — services.AddSingleton<DashboardConfig, XxxDashboardConfig>()
[5] GET /dm/dashboards/{code} — kiểm tra kết quả
```
