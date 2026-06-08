# 25 — Server-Driven UI (SDUI)

Backend trả về **cả layout lẫn dữ liệu** — frontend chỉ render theo cấu trúc nhận được,
không cần biết trước từng màn hình là gì.

> **Cần overview tổng hệ thống chart?** Đọc **[doc 51](./51-charts-system-overview.md)**
> — chỉ-mục đầy đủ các path (StagingRecord vs direct lakehouse), endpoint catalog,
> file structure, deploy status.

---

## 1. Khái niệm & So sánh với Dashboard Engine

> ⚠️ **Update 2026-06-08:** `Dashboard Engine` (`/dm/dashboards/{code}`) chỉ có code
> `DashboardEngine` + `DashboardConfig` đăng ký DI nhưng **controller chưa wire** —
> endpoint này chưa truy cập được qua HTTP. Hiện tại chỉ có **SDUI** (`/dm/pages/{code}`)
> sống. Bảng so sánh dưới đây mô tả thiết kế gốc — Dashboard Engine để dành.

| | Dashboard Engine (`/dm/dashboards`) ❌ chưa wire | SDUI (`/dm/pages`) ✅ sống |
|---|---|---|
| Backend trả về | Dữ liệu theo `sections[]` đã cố định shape | Toàn bộ layout: vị trí, span, màu sắc, actions |
| Frontend cần biết | Loại section nào ứng với component nào | Không cần biết gì — render generic |
| Thêm màn hình mới | Backend thêm config, FE cập nhật renderer | Backend thêm config, FE **không cần thay đổi gì** |
| Phù hợp | Báo cáo/dashboard đơn giản | Màn hình phức tạp, layout linh hoạt, nhiều màn |

**Dùng SDUI khi:** bạn có nhiều màn hình dashboard khác nhau về layout và muốn thêm
màn hình mới mà không triển khai lại frontend.

> **Path khác cho chart:** SDUI ở doc này đi qua **StagingRecord** (cần ingest data
> trước). Nếu muốn query thẳng lakehouse PG không ingest, dùng `/lakehouse/charts/{code}`
> — pattern Path B. Xem [doc 50](./50-add-new-lakehouse-chart-guide.md).

---

## 2. Luồng tổng quan

```
[HIS / bên thứ 3]
    │
    ├─ POST /dm/sources          (đăng ký mapping field HIS → canonical)
    ├─ POST /dm/ingest/json      (1 record)
    └─ POST /dm/ingest/file      (batch — JSON array hoặc CSV)
    │
    ▼
[StagingRecord — PostgreSQL]
    Status: Pending → Matched (MatchingWorker chạy mỗi 30s)
    │
    ▼
[GET /dm/pages/{code}]
    SduiEngine tìm SduiPageConfig theo code
    → Fetch StagingRecord (Status=Matched) song song theo RecordTypes
    → Parse CanonicalPayload → gọi config.BuildPage()
    → Trả SduiPage (title, badge, live, actions, rows[{components[]}])
    │
    ▼
[Frontend Next.js]
    Không biết gì về từng page — chỉ render theo type + span
    <PageRenderer page={page} />
```

---

## 3. API Endpoints

| Method | URL | Mô tả |
|--------|-----|-------|
| `GET`  | `/dm/pages` | Liệt kê page codes đã đăng ký |
| `GET`  | `/dm/pages/{code}` | Render SDUI page đầy đủ |

**Query params của `GET /dm/pages/{code}`:**

| Param | Bắt buộc | Mô tả |
|-------|----------|-------|
| `sourceSystem` | Không | Lọc theo nguồn. Bỏ trống = lấy tất cả nguồn |
| `date` | Không | Ngày báo cáo `yyyy-MM-dd`. Mặc định = hôm nay (UTC) |

---

## 4. Cấu trúc Response

```jsonc
{
  "success": true,
  "data": {
    "code":      "executive",
    "title":     "Bảng điều hành bệnh viện",
    "badge":     "Trực tiếp",
    "live":      true,
    "subtitle":  "Cập nhật: 14:32 · Ngày 28/05/2026",
    "actions": [
      { "label": "Xuất PDF", "variant": "default", "color": null },
      { "label": "Cài đặt", "variant": "default",  "color": null }
    ],
    "rows": [
      {
        "components": [
          {
            "type": "KpiCard",
            "span": 6,
            "props": {
              "title":     "Tổng bệnh nhân nội trú",
              "value":     13,
              "accent":    "#1677ff",
              "hint":      "đang điều trị",
              "hintColor": null
            }
          },
          {
            "type": "KpiCard",
            "span": 6,
            "props": {
              "title":     "BOR",
              "value":     "11.3%",
              "accent":    "#52c41a",
              "hint":      "công suất giường",
              "hintColor": null
            }
          }
          // ... 2 KpiCard nữa
        ]
      },
      {
        "components": [
          {
            "type": "ProgressList",
            "span": 8,
            "props": {
              "title":       "Công suất giường theo khoa",
              "headerAction": "Xem chi tiết",
              "maxValue":    100,
              "items": [
                { "label": "ICU (13/15)",           "value": 86.7, "secondaryValue": 90, "color": "#faad14" },
                { "label": "Nội tổng hợp (5/40)",   "value": 12.5, "secondaryValue": 90, "color": "#52c41a" }
              ],
              "footerActions": null
            }
          },
          {
            "type": "AlertList",
            "span": 16,
            "props": {
              "title":         "Cảnh báo lâm sàng",
              "realtimeBadge": true,
              "maxHeight":     400,
              "totalCount":    0,
              "items":         []
            }
          }
        ]
      },
      {
        "components": [
          {
            "type": "FlowPipeline",
            "span": 12,
            "props": {
              "title":  "Dòng bệnh nhân",
              "footer": "Tổng: 15 lượt",
              "stages": [
                { "label": "Chờ khám sàng", "value": 0,  "color": "#1677ff" },
                { "label": "Đang nội trú",  "value": 13, "color": "#52c41a" },
                { "label": "Chờ xuất viện", "value": 0,  "color": "#faad14" },
                { "label": "Đã xuất viện",  "value": 2,  "color": "#8c8c8c" }
              ]
            }
          },
          {
            "type": "ChartPie",
            "span": 12,
            "props": {
              "title":   "Đối tượng KCB",
              "height":  260,
              "variant": "donut",
              "legend":  true,
              "data": [
                { "label": "BHYT", "value": 10 },
                { "label": "DV",   "value": 2  },
                { "label": "Khác", "value": 1  }
              ],
              "colors": ["#1677ff", "#52c41a", "#faad14", "#ff4d4f", "#722ed1"]
            }
          }
        ]
      }
    ],
    "generatedAt": "2026-05-28T07:14:00Z"
  }
}
```

---

## 5. Component Types

Grid 24 cột (Ant Design style). `span` là số cột chiếm (1–24).

### KpiCard

```jsonc
{
  "type": "KpiCard",
  "span": 6,           // 4 cards trên 1 row
  "props": {
    "title":     "Tổng bệnh nhân",   // tiêu đề card
    "value":     13,                 // int hoặc string ("11.3%")
    "accent":    "#1677ff",          // màu border / icon
    "hint":      "đang điều trị",    // dòng phụ bên dưới
    "hintColor": null                // màu hint, null = mặc định
  }
}
```

### ProgressList

```jsonc
{
  "type": "ProgressList",
  "span": 8,
  "props": {
    "title":        "Công suất giường theo khoa",
    "headerAction": "Xem chi tiết",   // link/button ở header, null = ẩn
    "maxValue":     100,              // giá trị tối đa của progress bar
    "items": [
      {
        "label":          "ICU (13/15)",
        "value":          86.7,       // giá trị hiện tại (vd: %)
        "secondaryValue": 90,         // ngưỡng cảnh báo
        "color":          "#faad14"   // màu bar
      }
    ],
    "footerActions": null             // [{label, variant}] hoặc null
  }
}
```

**Màu bar gợi ý:** `#52c41a` (bình thường) | `#faad14` (cảnh báo) | `#ff4d4f` (nguy hiểm)

### AlertList

```jsonc
{
  "type": "AlertList",
  "span": 16,
  "props": {
    "title":         "Cảnh báo lâm sàng",
    "realtimeBadge": true,      // badge "LIVE" ở header
    "maxHeight":     400,       // chiều cao tối đa (px), scroll nếu vượt
    "totalCount":    3,         // tổng số cảnh báo (kể cả chưa load)
    "items": [
      {
        "code":     "Troponin I",   // mã chỉ số / loại cảnh báo
        "text":     "Kết quả bất thường: 2.4 ng/mL",
        "patient":  "Nguyễn Văn An",
        "dept":     "ICU",
        "time":     "3 phút trước",
        "severity": "critical"      // "critical" | "warning" | "info"
      }
    ]
  }
}
```

### FlowPipeline

```jsonc
{
  "type": "FlowPipeline",
  "span": 12,
  "props": {
    "title":  "Dòng bệnh nhân",
    "footer": "Tổng: 15 lượt",   // text chú thích bên dưới, null = ẩn
    "stages": [
      { "label": "Chờ khám sàng", "value": 0,  "color": "#1677ff" },
      { "label": "Đang nội trú",  "value": 13, "color": "#52c41a" },
      { "label": "Chờ xuất viện", "value": 0,  "color": "#faad14" },
      { "label": "Đã xuất viện",  "value": 2,  "color": "#8c8c8c" }
    ]
  }
}
```

### ChartPie

```jsonc
{
  "type": "ChartPie",
  "span": 12,
  "props": {
    "title":   "Đối tượng KCB",
    "height":  260,              // chiều cao chart (px)
    "variant": "donut",          // "pie" | "donut"
    "legend":  true,             // hiện legend
    "data": [
      { "label": "BHYT", "value": 10 },
      { "label": "DV",   "value": 2  }
    ],
    "colors": ["#1677ff", "#52c41a", "#faad14", "#ff4d4f", "#722ed1"]
  }
}
```

---

## 6. Test từ đầu đến cuối — Executive Page

Page mẫu `executive` dùng cùng dữ liệu với dashboard M02:
record types `benh-nhan-noi-tru` và `cau-hinh-giuong`.

Nếu đã làm **Bước 1–5** ở [doc 24](24-dashboard-data-matching.md) thì bỏ qua —
data đã có sẵn, gọi thẳng bước 6.

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
    "chan_doan":  "ChanDoan"
  }
}
```

### Bước 2 — Đăng ký SourceProfile cấu hình giường

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

### Bước 3 — Ingest cấu hình giường

Lưu file `giuong.json`:

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

### Bước 4 — Ingest 15 bệnh nhân mẫu

Lưu file `benh-nhan.json`:

```json
[
  { "ma_bn": "BN26000001", "ho_ten": "Nguyễn Văn An",  "ten_khoa": "Nội tổng hợp", "so_giuong": "NTH-01", "ngay_nhap": "2026-05-24", "ngay_xuat": null,         "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",  "ma_icd": "J18.9", "ten_icd": "Viêm phổi",   "chan_doan": "Viêm phổi cấp" },
  { "ma_bn": "BN26000002", "ho_ten": "Trần Thị Bình",  "ten_khoa": "Nội tổng hợp", "so_giuong": "NTH-02", "ngay_nhap": "2026-05-22", "ngay_xuat": null,         "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",  "ma_icd": "A41.9","ten_icd": "Nhiễm khuẩn huyết", "chan_doan": "Sepsis" },
  { "ma_bn": "BN26000003", "ho_ten": "Lê Minh Cường",  "ten_khoa": "Ngoại khoa",   "so_giuong": "NG-08",  "ngay_nhap": "2026-05-26", "ngay_xuat": null,         "doi_tuong": "DV",   "trang_thai": "DangNoiTru",  "ma_icd": "K35.2","ten_icd": "Viêm ruột thừa",    "chan_doan": "Appendicitis" },
  { "ma_bn": "BN26000004", "ho_ten": "Phạm Thị Dung",  "ten_khoa": "Sản khoa",     "so_giuong": "SAN-04", "ngay_nhap": "2026-05-25", "ngay_xuat": "2026-05-28", "doi_tuong": "BHYT", "trang_thai": "DaXuatVien",  "ma_icd": "Z39.0","ten_icd": "Hậu sản bình thường","chan_doan": "Sau sinh thường" },
  { "ma_bn": "BN26000005", "ho_ten": "Hoàng Văn Đức",  "ten_khoa": "ICU",          "so_giuong": "ICU-01", "ngay_nhap": "2026-05-20", "ngay_xuat": null,         "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",  "ma_icd": "I21.0","ten_icd": "STEMI",             "chan_doan": "STEMI" },
  { "ma_bn": "BN26000006", "ho_ten": "Vũ Thị Hoa",     "ten_khoa": "ICU",          "so_giuong": "ICU-02", "ngay_nhap": "2026-05-21", "ngay_xuat": null,         "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",  "ma_icd": "J80",  "ten_icd": "ARDS",              "chan_doan": "ARDS" },
  { "ma_bn": "BN26000007", "ho_ten": "Đặng Minh Tuấn", "ten_khoa": "Nội tổng hợp", "so_giuong": "NTH-05", "ngay_nhap": "2026-05-28", "ngay_xuat": null,         "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",  "ma_icd": "J18.9","ten_icd": "Viêm phổi",         "chan_doan": "Viêm phổi cấp" },
  { "ma_bn": "BN26000008", "ho_ten": "Ngô Thị Lan",    "ten_khoa": "Nhi khoa",     "so_giuong": "NHI-03", "ngay_nhap": "2026-05-27", "ngay_xuat": null,         "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",  "ma_icd": "J18.9","ten_icd": "Viêm phổi",         "chan_doan": "Viêm phổi trẻ em" },
  { "ma_bn": "BN26000009", "ho_ten": "Bùi Văn Hải",    "ten_khoa": "Ngoại khoa",   "so_giuong": "NG-12",  "ngay_nhap": "2026-05-28", "ngay_xuat": null,         "doi_tuong": "DV",   "trang_thai": "DangNoiTru",  "ma_icd": "S72.0","ten_icd": "Gãy cổ xương đùi",  "chan_doan": "Gãy cổ xương đùi" },
  { "ma_bn": "BN26000010", "ho_ten": "Đinh Thị Mai",   "ten_khoa": "Sản khoa",     "so_giuong": "SAN-07", "ngay_nhap": "2026-05-28", "ngay_xuat": null,         "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",  "ma_icd": "O20.0","ten_icd": "Dọa sảy thai",       "chan_doan": "Dọa sảy thai 12 tuần" },
  { "ma_bn": "BN26000011", "ho_ten": "Trịnh Văn Nam",  "ten_khoa": "ICU",          "so_giuong": "ICU-03", "ngay_nhap": "2026-05-19", "ngay_xuat": null,         "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",  "ma_icd": "I63.5","ten_icd": "Đột quỵ nhồi máu",  "chan_doan": "Đột quỵ nhồi máu não" },
  { "ma_bn": "BN26000012", "ho_ten": "Lý Thị Phương",  "ten_khoa": "Nội tổng hợp", "so_giuong": "NTH-08", "ngay_nhap": "2026-05-23", "ngay_xuat": null,         "doi_tuong": "Khac", "trang_thai": "DangNoiTru",  "ma_icd": "E11.9","ten_icd": "ĐTĐ type 2",         "chan_doan": "ĐTĐ type 2" },
  { "ma_bn": "BN26000013", "ho_ten": "Phan Văn Khánh", "ten_khoa": "Ngoại khoa",   "so_giuong": "NG-15",  "ngay_nhap": "2026-05-26", "ngay_xuat": null,         "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",  "ma_icd": "C34.1","ten_icd": "Ung thư phổi",       "chan_doan": "UTPQ thuỳ trên" },
  { "ma_bn": "BN26000014", "ho_ten": "Cao Thị Xuân",   "ten_khoa": "Nhi khoa",     "so_giuong": "NHI-06", "ngay_nhap": "2026-05-27", "ngay_xuat": "2026-05-28", "doi_tuong": "BHYT", "trang_thai": "DaXuatVien",  "ma_icd": "A09",  "ten_icd": "Tiêu chảy cấp",     "chan_doan": "Tiêu chảy cấp" },
  { "ma_bn": "BN26000015", "ho_ten": "Dương Minh Khoa","ten_khoa": "ICU",          "so_giuong": "ICU-05", "ngay_nhap": "2026-05-28", "ngay_xuat": null,         "doi_tuong": "DV",   "trang_thai": "DangNoiTru",  "ma_icd": "B08.4","ten_icd": "Tay chân miệng",     "chan_doan": "TCM độ 3" }
]
```

```http
POST http://localhost:5004/dm/ingest/file
Content-Type: multipart/form-data

sourceSystem: his-01
recordType:   benh-nhan-noi-tru
file:         benh-nhan.json
```

### Bước 5 — Chờ MatchingWorker

MatchingWorker chạy nền mỗi **30 giây**, tự chuyển `Pending → Matched`.
Kiểm tra nếu cần:

```http
GET http://localhost:5004/dm/records?sourceSystem=his-01&recordType=benh-nhan-noi-tru
```

### Bước 6 — Gọi SDUI page

```http
GET http://localhost:5004/dm/pages/executive?sourceSystem=his-01&date=2026-05-28
```

**Kết quả mong đợi (với 15 bệnh nhân mẫu):**

| Component | Nội dung mong đợi |
|-----------|-------------------|
| KpiCard "Tổng bệnh nhân nội trú" | `13` (15 − 2 đã xuất) |
| KpiCard "BOR" | `~11.3%` (13 / 115 giường tổng) |
| KpiCard "Vào viện hôm nay" | `4` (BN007, BN009, BN010, BN015) |
| KpiCard "Ra viện hôm nay" | `2` (BN004, BN014) |
| ProgressList ICU | `86.7%` (13/15) — màu vàng |
| FlowPipeline "Đang nội trú" | `13` |
| ChartPie | BHYT: 10, DV: 3, Khác: 1 (tính cả BN đã xuất) |

**Liệt kê pages:**
```http
GET http://localhost:5004/dm/pages
```
→ `["executive"]`

---

## 7. Cấu trúc code trong codebase

```
DataMatchingService.Application/
  Sdui/
    SduiComponent.cs      ← abstract SduiComponent + 5 types (JsonPolymorphic)
    SduiPage.cs           ← SduiPage, SduiRow, SduiAction records
    SduiPageConfig.cs     ← abstract base — override Code, RecordTypes, BuildPage()
    SduiEngine.cs         ← fetch song song, parse JSON, gọi BuildPage()
    Pages/
      ExecutiveSduiConfig.cs   ← page "executive" — reference implementation

DataMatchingService.API/
  Controllers/
    PagesController.cs    ← GET /dm/pages, GET /dm/pages/{code}
```

### Cách SduiEngine hoạt động

```
SduiEngine.ExecuteAsync("executive", "his-01", date)
  │
  ├─ registry["executive"] → ExecutiveSduiConfig
  │
  ├─ Task.WhenAll:
  │    GetMatchedAsync("his-01", "benh-nhan-noi-tru")
  │    GetMatchedAsync("his-01", "cau-hinh-giuong")
  │
  ├─ ParsePayloads() → Dictionary<string, JsonElement> mỗi record
  │
  └─ ExecutiveSduiConfig.BuildPage(data, date) → SduiPage
```

---

## 8. Thêm page mới (ví dụ: Phẫu thuật)

### Bước 1 — Tạo config file

`Application/Sdui/Pages/SurgeryPageConfig.cs`:

```csharp
using System.Text.Json;

namespace Hdos.DataMatchingService.Application.Sdui.Pages;

public sealed class SurgeryPageConfig : SduiPageConfig
{
    public override string Code => "surgery";

    public override IReadOnlyList<string> RecordTypes => ["phau-thuat"];

    public override SduiPage BuildPage(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate)
    {
        var rows = data.GetValueOrDefault("phau-thuat", []);

        int total     = rows.Count;
        int today     = rows.Count(r => Date(r, "NgayPhauThuat") == reportDate);
        int success   = rows.Count(r => Str(r, "KetQua") == "Thành công");

        return new SduiPage(
            Code:        Code,
            Title:       "Báo cáo Phẫu thuật",
            Badge:       null,
            Live:        false,
            Subtitle:    $"Ngày {reportDate:dd/MM/yyyy}",
            Actions:     [new("Xuất PDF", "default", null)],
            Rows: [
                new([
                    new KpiCardComponent(8, new("Tổng ca phẫu thuật", total,   "ca", null)),
                    new KpiCardComponent(8, new("Hôm nay",             today,   "ca", null)),
                    new KpiCardComponent(8, new("Thành công",          success, "ca", "#52c41a")),
                ]),
                new([
                    BuildLoaiPtChart(rows),
                    BuildFlowPt(rows),
                ]),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    private static ChartPieComponent BuildLoaiPtChart(List<Dictionary<string, JsonElement>> rows)
    {
        var data = rows
            .GroupBy(r => Str(r, "LoaiPhauThuat") ?? "Khác")
            .Select(g => new ChartPieData(g.Key, g.Count()))
            .ToList();

        return new ChartPieComponent(12, new ChartPieProps(
            Title:   "Phân loại phẫu thuật",
            Height:  260,
            Variant: "donut",
            Legend:  true,
            Data:    data,
            Colors:  ["#1677ff", "#52c41a", "#faad14", "#ff4d4f"]));
    }

    private static FlowPipelineComponent BuildFlowPt(List<Dictionary<string, JsonElement>> rows)
    {
        return new FlowPipelineComponent(12, new FlowPipelineProps(
            Title:  "Tình trạng ca mổ",
            Footer: null,
            Stages: [
                new("Đã lên lịch",   rows.Count(r => Str(r, "TrangThai") == "DaLenLich"),  "#1677ff"),
                new("Đang phẫu thuật",rows.Count(r => Str(r, "TrangThai") == "DangMo"),    "#faad14"),
                new("Hoàn thành",    rows.Count(r => Str(r, "TrangThai") == "HoanThanh"),  "#52c41a"),
            ]));
    }
}
```

### Bước 2 — Đăng ký DI (1 dòng)

`Application/DependencyInjection.cs`:

```csharp
services.AddSingleton<SduiPageConfig, ExecutiveSduiConfig>();
services.AddSingleton<SduiPageConfig, SurgeryPageConfig>(); // ← thêm dòng này
services.AddSingleton<SduiEngine>();
```

### Bước 3 — Gọi API

```http
GET /dm/pages/surgery?sourceSystem=his-01&date=2026-05-28
```

**Không cần sửa gì ở Controller hay Frontend.**

---

## 9. Frontend — TypeScript types & renderer

### Types

```typescript
// types/sdui.ts

export type SduiVariant = 'primary' | 'default' | 'danger'
export type Severity     = 'critical' | 'warning' | 'info'

export interface SduiAction {
  label:   string
  variant: SduiVariant
  color:   string | null
}

// --- Component props ---

export interface KpiCardProps {
  title:     string
  value:     number | string
  accent:    string | null
  hint:      string | null
  hintColor: string | null
}

export interface ProgressItem {
  label:          string
  value:          number
  secondaryValue: number | null
  color:          string | null
}

export interface ProgressListProps {
  title:         string
  headerAction:  string | null
  maxValue:      number
  items:         ProgressItem[]
  footerActions: { label: string; variant: string }[] | null
}

export interface AlertItem {
  code:     string
  text:     string
  patient:  string
  dept:     string
  time:     string
  severity: Severity
}

export interface AlertListProps {
  title:         string
  realtimeBadge: boolean
  maxHeight:     number | null
  totalCount:    number
  items:         AlertItem[]
}

export interface FlowStage {
  label: string
  value: number
  color: string | null
}

export interface FlowPipelineProps {
  title:  string
  footer: string | null
  stages: FlowStage[]
}

export interface ChartPieData { label: string; value: number }

export interface ChartPieProps {
  title:   string
  height:  number | null
  variant: 'pie' | 'donut' | null
  legend:  boolean
  data:    ChartPieData[]
  colors:  string[] | null
}

// --- Discriminated union ---

export type SduiComponent =
  | { type: 'KpiCard';      span: number | null; props: KpiCardProps      }
  | { type: 'ProgressList'; span: number | null; props: ProgressListProps }
  | { type: 'AlertList';    span: number | null; props: AlertListProps    }
  | { type: 'FlowPipeline'; span: number | null; props: FlowPipelineProps }
  | { type: 'ChartPie';     span: number | null; props: ChartPieProps     }

export interface SduiRow      { components: SduiComponent[] }

export interface SduiPage {
  code:        string
  title:       string
  badge:       string | null
  live:        boolean
  subtitle:    string | null
  actions:     SduiAction[]
  rows:        SduiRow[]
  generatedAt: string
}
```

### Renderer (Next.js / React)

```tsx
// components/SduiPageRenderer.tsx
'use client'
import { SduiPage, SduiComponent } from '@/types/sdui'
import { Col, Row } from 'antd'

export function SduiPageRenderer({ page }: { page: SduiPage }) {
  return (
    <div>
      <header style={{ marginBottom: 16 }}>
        <h1>{page.title} {page.badge && <span className="badge">{page.badge}</span>}</h1>
        {page.subtitle && <p className="subtitle">{page.subtitle}</p>}
        <div className="actions">
          {page.actions.map(a => (
            <button key={a.label} data-variant={a.variant}>{a.label}</button>
          ))}
        </div>
      </header>

      {page.rows.map((row, i) => (
        <Row key={i} gutter={[16, 16]} style={{ marginBottom: 16 }}>
          {row.components.map((comp, j) => (
            <Col key={j} span={comp.span ?? 24}>
              <ComponentRenderer component={comp} />
            </Col>
          ))}
        </Row>
      ))}
    </div>
  )
}

function ComponentRenderer({ component }: { component: SduiComponent }) {
  switch (component.type) {
    case 'KpiCard':      return <KpiCard      {...component.props} />
    case 'ProgressList': return <ProgressList {...component.props} />
    case 'AlertList':    return <AlertList    {...component.props} />
    case 'FlowPipeline': return <FlowPipeline {...component.props} />
    case 'ChartPie':     return <ChartPie     {...component.props} />
  }
}
```

### Fetch page (SWR)

```typescript
// hooks/useSduiPage.ts
import useSWR from 'swr'
import { SduiPage } from '@/types/sdui'

const fetcher = (url: string) =>
  fetch(url).then(r => r.json()).then(r => r.data as SduiPage)

export function useSduiPage(code: string, sourceSystem?: string, date?: string) {
  const params = new URLSearchParams()
  if (sourceSystem) params.set('sourceSystem', sourceSystem)
  if (date)         params.set('date', date)

  return useSWR<SduiPage>(
    `/dm/pages/${code}?${params}`,
    fetcher,
    { refreshInterval: 60_000 } // tự refresh mỗi 1 phút
  )
}
```

### Trang executive

```tsx
// app/dashboard/executive/page.tsx
import { useSduiPage }       from '@/hooks/useSduiPage'
import { SduiPageRenderer }  from '@/components/SduiPageRenderer'

export default function ExecutiveDashboard() {
  const { data, isLoading } = useSduiPage('executive', 'his-01')

  if (isLoading) return <div>Đang tải...</div>
  if (!data)     return <div>Không tìm thấy page</div>

  return <SduiPageRenderer page={data} />
}
```

---

## 10. Thêm page mới — Checklist

```
[1] POST /dm/sources              — đăng ký SourceProfile + mappings (1 lần/record type)
[2] POST /dm/ingest/file          — upload dữ liệu mẫu để test
[3] Tạo XxxPageConfig.cs          — override Code, RecordTypes, BuildPage()
                                    Đặt vào Application/Sdui/Pages/
[4] +1 dòng DI                    — services.AddSingleton<SduiPageConfig, XxxPageConfig>()
[5] GET /dm/pages/{code}          — kiểm tra response
[6] Frontend                      — <SduiPageRenderer page={data} /> (không cần thêm gì)
```

---

## 11. Canonical fields cho page `executive`

Xem chi tiết ở [doc 24 — mục 7](24-dashboard-data-matching.md#7-cấu-trúc-code-trong-codebase).

**Tóm tắt:**

| RecordType | Field quan trọng | Dùng để |
|------------|-----------------|---------|
| `benh-nhan-noi-tru` | `TrangThai`, `NgayNhap`, `NgayXuat` | KPI, FlowPipeline |
| `benh-nhan-noi-tru` | `TenKhoa`, `DoiTuong` | ProgressList, ChartPie |
| `cau-hinh-giuong` | `TenKhoa`, `TongGiuong` | BOR%, ProgressList maxValue |
| `benh-nhan-noi-tru` | `CanhBao` (nếu có) | AlertList |
