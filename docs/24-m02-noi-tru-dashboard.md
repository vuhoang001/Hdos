# 24 — M02: Dashboard Trực quan Nội trú

API tổng hợp dữ liệu bệnh nhân nội trú từ HIS thành dashboard M02, gồm 4 section:
KPI summary, phân loại đối tượng KCB, Top 10 ICD, danh sách bệnh nhân.

---

## Endpoint

```
GET /dm/reports/m02?sourceSystem=his-01&date=2026-05-28
```

| Query param    | Bắt buộc | Mô tả |
|----------------|----------|-------|
| `sourceSystem` | Không    | Lọc theo source HIS (vd: `his-01`). Bỏ qua = lấy tất cả. |
| `date`         | Không    | Ngày báo cáo `yyyy-MM-dd`. Mặc định = hôm nay (UTC). |

---

## Cấu trúc Response

```jsonc
{
  "reportDate": "2026-05-28",
  "generatedAt": "2026-05-28T09:14:00Z",
  "summary": {
    "tongGiuongDangSuDung": 12,   // số giường đang có bệnh nhân (= dangDieuTri)
    "tongGiuongKhaDung": 15,      // tổng sức chứa (từ RecordType "cau-hinh-giuong")
    "borPercent": 80.0,           // = dangSuDung / khaDung * 100
    "dangDieuTri": 12,
    "vaoVienHomNay": 3,
    "raVienHomNay": 1,
    "alos": 4.2                   // Average Length of Stay (ngày)
  },
  "doiTuongKcb": [
    { "doiTuong": "BHYT",  "soLuong": 9,  "phanTram": 75.0 },
    { "doiTuong": "DV",    "soLuong": 2,  "phanTram": 16.7 },
    { "doiTuong": "Khac",  "soLuong": 1,  "phanTram": 8.3  }
  ],
  "topIcd": [
    { "maIcd": "J18.9", "tenIcd": "Viêm phổi, không xác định", "soLuong": 3 }
  ],
  "benhNhanNoiTru": [
    {
      "mrn": "BN26000001",
      "tenBenhNhan": "Nguyễn Văn An",
      "tenKhoa": "Nội tổng hợp",
      "soGiuong": "NTH-01",
      "ngayNhap": "2026-05-24",
      "ngayXuat": null,
      "doiTuong": "BHYT",
      "trangThai": "DangNoiTru",
      "chanDoan": "Viêm phổi"
    }
  ]
}
```

---

## Canonical Fields — RecordType `benh-nhan-noi-tru`

Đây là tên field trong `CanonicalPayload` sau khi mapping từ HIS.

| Canonical field | Kiểu   | Bắt buộc | Mô tả |
|-----------------|--------|----------|-------|
| `MRN`           | string | Có       | Mã bệnh nhân |
| `TenBenhNhan`   | string | Có       | Họ tên bệnh nhân |
| `TenKhoa`       | string | Có       | Tên khoa điều trị |
| `SoGiuong`      | string | Không    | Số giường (vd: `ICU-03`) |
| `NgayNhap`      | string | Có       | Ngày nhập viện `yyyy-MM-dd` |
| `NgayXuat`      | string | Không    | Ngày xuất viện `yyyy-MM-dd`, null nếu còn nằm |
| `DoiTuong`      | string | Có       | `BHYT` / `DV` / `Khac` |
| `TrangThai`     | string | Có       | `DangNoiTru` / `DaXuatVien` |
| `MaICD`         | string | Không    | Mã ICD-10 (vd: `J18.9`) |
| `TenICD`        | string | Không    | Tên ICD đầy đủ |
| `ChanDoan`      | string | Không    | Chẩn đoán nhập viện |

---

## Canonical Fields — RecordType `cau-hinh-giuong`

Dùng để tính BOR%. Nếu không có thì `tongGiuongKhaDung = 0` và `borPercent = 0`.

| Canonical field | Kiểu | Mô tả |
|-----------------|------|-------|
| `TenKhoa`       | string | Tên khoa |
| `TongGiuong`    | number | Tổng số giường của khoa |

---

## Hướng dẫn test từng bước

### Bước 1 — Đăng ký SourceProfile cho bệnh nhân nội trú

```http
POST http://localhost:5004/dm/sources
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "recordType": "benh-nhan-noi-tru",
  "displayName": "HIS - Bệnh nhân nội trú",
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

### Bước 2 — Đăng ký SourceProfile cho cấu hình giường

```http
POST http://localhost:5004/dm/sources
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "recordType": "cau-hinh-giuong",
  "displayName": "HIS - Cấu hình giường",
  "businessKeyField": "TenKhoa",
  "mappings": {
    "ten_khoa":    "TenKhoa",
    "tong_giuong": "TongGiuong"
  }
}
```

### Bước 3 — Ingest cấu hình giường

```http
POST http://localhost:5004/dm/ingest/json
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "recordType": "cau-hinh-giuong",
  "records": [
    { "ten_khoa": "Nội tổng hợp", "tong_giuong": 40 },
    { "ten_khoa": "ICU",          "tong_giuong": 15 },
    { "ten_khoa": "Ngoại khoa",   "tong_giuong": 35 },
    { "ten_khoa": "Sản khoa",     "tong_giuong": 30 },
    { "ten_khoa": "Nhi khoa",     "tong_giuong": 25 }
  ]
}
```

### Bước 4 — Ingest bệnh nhân nội trú (15 bệnh nhân mẫu)

```http
POST http://localhost:5004/dm/ingest/json
Content-Type: application/json

{
  "sourceSystem": "his-01",
  "recordType": "benh-nhan-noi-tru",
  "records": [
    {
      "ma_bn": "BN26000001", "ho_ten": "Nguyễn Văn An",
      "ten_khoa": "Nội tổng hợp", "so_giuong": "NTH-01",
      "ngay_nhap": "2026-05-24", "ngay_xuat": null,
      "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
      "ma_icd": "J18.9", "ten_icd": "Viêm phổi, không xác định",
      "chan_doan": "Viêm phổi"
    },
    {
      "ma_bn": "BN26000002", "ho_ten": "Trần Thị Bình",
      "ten_khoa": "Nội tổng hợp", "so_giuong": "NTH-02",
      "ngay_nhap": "2026-05-22", "ngay_xuat": null,
      "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
      "ma_icd": "A41.9", "ten_icd": "Nhiễm khuẩn huyết, không xác định",
      "chan_doan": "Sepsis"
    },
    {
      "ma_bn": "BN26000003", "ho_ten": "Lê Minh Cường",
      "ten_khoa": "Ngoại khoa", "so_giuong": "NG-08",
      "ngay_nhap": "2026-05-26", "ngay_xuat": null,
      "doi_tuong": "DV", "trang_thai": "DangNoiTru",
      "ma_icd": "K35.2", "ten_icd": "Viêm ruột thừa cấp có abces",
      "chan_doan": "Appendicitis"
    },
    {
      "ma_bn": "BN26000004", "ho_ten": "Phạm Thị Dung",
      "ten_khoa": "Sản khoa", "so_giuong": "SAN-04",
      "ngay_nhap": "2026-05-25", "ngay_xuat": "2026-05-28",
      "doi_tuong": "BHYT", "trang_thai": "DaXuatVien",
      "ma_icd": "Z39.0", "ten_icd": "Hậu sản bình thường",
      "chan_doan": "Sau sinh thường"
    },
    {
      "ma_bn": "BN26000005", "ho_ten": "Hoàng Văn Đức",
      "ten_khoa": "ICU", "so_giuong": "ICU-01",
      "ngay_nhap": "2026-05-20", "ngay_xuat": null,
      "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
      "ma_icd": "I21.0", "ten_icd": "NMCT cấp ST chênh lên (STEMI)",
      "chan_doan": "STEMI"
    },
    {
      "ma_bn": "BN26000006", "ho_ten": "Vũ Thị Hoa",
      "ten_khoa": "ICU", "so_giuong": "ICU-02",
      "ngay_nhap": "2026-05-21", "ngay_xuat": null,
      "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
      "ma_icd": "J80", "ten_icd": "Hội chứng suy hô hấp cấp (ARDS)",
      "chan_doan": "ARDS"
    },
    {
      "ma_bn": "BN26000007", "ho_ten": "Đặng Minh Tuấn",
      "ten_khoa": "Nội tổng hợp", "so_giuong": "NTH-05",
      "ngay_nhap": "2026-05-28", "ngay_xuat": null,
      "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
      "ma_icd": "J18.9", "ten_icd": "Viêm phổi, không xác định",
      "chan_doan": "Viêm phổi cấp"
    },
    {
      "ma_bn": "BN26000008", "ho_ten": "Ngô Thị Lan",
      "ten_khoa": "Nhi khoa", "so_giuong": "NHI-03",
      "ngay_nhap": "2026-05-27", "ngay_xuat": null,
      "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
      "ma_icd": "J18.9", "ten_icd": "Viêm phổi, không xác định",
      "chan_doan": "Viêm phổi trẻ em"
    },
    {
      "ma_bn": "BN26000009", "ho_ten": "Bùi Văn Hải",
      "ten_khoa": "Ngoại khoa", "so_giuong": "NG-12",
      "ngay_nhap": "2026-05-28", "ngay_xuat": null,
      "doi_tuong": "DV", "trang_thai": "DangNoiTru",
      "ma_icd": "S72.0", "ten_icd": "Gãy cổ xương đùi",
      "chan_doan": "Gãy cổ xương đùi phải"
    },
    {
      "ma_bn": "BN26000010", "ho_ten": "Đinh Thị Mai",
      "ten_khoa": "Sản khoa", "so_giuong": "SAN-07",
      "ngay_nhap": "2026-05-28", "ngay_xuat": null,
      "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
      "ma_icd": "O20.0", "ten_icd": "Dọa sảy thai",
      "chan_doan": "Dọa sảy thai 12 tuần"
    },
    {
      "ma_bn": "BN26000011", "ho_ten": "Trịnh Văn Nam",
      "ten_khoa": "ICU", "so_giuong": "ICU-03",
      "ngay_nhap": "2026-05-19", "ngay_xuat": null,
      "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
      "ma_icd": "I63.5", "ten_icd": "Nhồi máu não do tắc ĐM não",
      "chan_doan": "Đột quỵ nhồi máu não"
    },
    {
      "ma_bn": "BN26000012", "ho_ten": "Lý Thị Phương",
      "ten_khoa": "Nội tổng hợp", "so_giuong": "NTH-08",
      "ngay_nhap": "2026-05-23", "ngay_xuat": null,
      "doi_tuong": "Khac", "trang_thai": "DangNoiTru",
      "ma_icd": "E11.9", "ten_icd": "Đái tháo đường type 2",
      "chan_doan": "ĐTĐ type 2 kiểm soát kém"
    },
    {
      "ma_bn": "BN26000013", "ho_ten": "Phan Văn Khánh",
      "ten_khoa": "Ngoại khoa", "so_giuong": "NG-15",
      "ngay_nhap": "2026-05-26", "ngay_xuat": null,
      "doi_tuong": "BHYT", "trang_thai": "DangNoiTru",
      "ma_icd": "C34.1", "ten_icd": "Ung thư phổi thuỳ trên",
      "chan_doan": "UTPQ thuỳ trên phổi trái"
    },
    {
      "ma_bn": "BN26000014", "ho_ten": "Cao Thị Xuân",
      "ten_khoa": "Nhi khoa", "so_giuong": "NHI-06",
      "ngay_nhap": "2026-05-27", "ngay_xuat": "2026-05-28",
      "doi_tuong": "BHYT", "trang_thai": "DaXuatVien",
      "ma_icd": "A09", "ten_icd": "Tiêu chảy cấp",
      "chan_doan": "Tiêu chảy cấp mất nước"
    },
    {
      "ma_bn": "BN26000015", "ho_ten": "Dương Minh Khoa",
      "ten_khoa": "ICU", "so_giuong": "ICU-05",
      "ngay_nhap": "2026-05-28", "ngay_xuat": null,
      "doi_tuong": "DV", "trang_thai": "DangNoiTru",
      "ma_icd": "B08.4", "ten_icd": "Bệnh tay chân miệng",
      "chan_doan": "Tay chân miệng độ 3"
    }
  ]
}
```

### Bước 5 — Gọi báo cáo

```http
GET http://localhost:5004/dm/reports/m02?sourceSystem=his-01&date=2026-05-28
```

**Kết quả mong đợi:**

- `dangDieuTri` = 12 (15 records, 3 đã xuất viện trừ BN26000004 và BN26000014, nhưng BN26000004 xuất ngày 28 → raVienHomNay=2)

  > Thực tế: `DangNoiTru` = BN001, 002, 003, 005, 006, 007, 008, 009, 010, 011, 012, 013, 015 = **13 bệnh nhân**

- `vaoVienHomNay` = 4 (BN007, BN009, BN010, BN015 nhập ngày 2026-05-28)
- `raVienHomNay` = 2 (BN004, BN014 xuất ngày 2026-05-28)
- `borPercent` ≈ 10.9% (13 / 119 giường tổng)
- Top ICD: J18.9 dẫn đầu với 3 bệnh nhân (BN001, BN007, BN008)

---

## Sơ đồ luồng dữ liệu

```
HIS (bên thứ 3)
    │
    │ 1. POST /dm/sources (đăng ký 1 lần)
    │ 2. POST /dm/ingest/json (mỗi ca / batch)
    ▼
IngestController
    → tra SourceProfile → apply mappings
    → lưu StagingRecord (Status=Pending, CanonicalPayload=JSON chuẩn)
    │
    │ background worker (MassTransit / HostedService)
    ▼
MatchingWorker
    → phát hiện trùng (PayloadHash)
    → MarkMatched / MarkDuplicate / MarkFailed
    │
    │ bất kỳ lúc nào sau khi Matched
    ▼
GET /dm/reports/m02
    → đọc StagingRecord (Status=Matched, RecordType=benh-nhan-noi-tru)
    → parse CanonicalPayload, aggregate
    → trả M02ReportDto
```

---

## Ghi chú

- **Trùng lặp**: nếu HIS push lại cùng MRN với payload không đổi, record sẽ `Duplicate` và không ảnh hưởng báo cáo.
- **Cập nhật bệnh nhân** (vd: đổi TrangThai từ `DangNoiTru` → `DaXuatVien`): push lại record mới — hệ thống tạo StagingRecord mới với hash khác, record cũ vẫn còn. Báo cáo lấy **tất cả Matched records** nên cả hai xuất hiện. Nếu cần dedup theo MRN, cần thêm logic "latest per MRN" vào handler.
- **Tần suất ingest**: HIS có thể push mỗi 60 phút (batch snapshot) hoặc real-time mỗi sự kiện nhập/xuất viện.
