# 23 — DataMatchingService

**DataMatchingService** là service nhận dữ liệu thô từ nhiều nguồn bên ngoài (HIS, BHYT, phòng khám…), chuẩn hóa về schema chung, phát hiện trùng lặp, và tổng hợp báo cáo nghiệp vụ y tế.

Khác với các service còn lại dùng SQL Server, DataMatchingService dùng **PostgreSQL** riêng — phù hợp với khối lượng ghi lớn (ingest batch), và không phụ thuộc instance SQL Server chung.

---

## Mục lục

1. [Tư duy thiết kế](#1-tư-duy-thiết-kế)
2. [Luồng hoạt động](#2-luồng-hoạt-động)
3. [Domain Model](#3-domain-model)
4. [API Reference](#4-api-reference)
5. [Hướng dẫn sử dụng từ đầu đến cuối](#5-hướng-dẫn-sử-dụng-từ-đầu-đến-cuối)
6. [Deduplication — SHA-256](#6-deduplication--sha-256)
7. [MatchingWorker](#7-matchingworker)
8. [Database — PostgreSQL](#8-database--postgresql)
9. [Performance & Scalability](#9-performance--scalability)
10. [Kiến trúc nội bộ](#10-kiến-trúc-nội-bộ)
11. [Chạy local](#11-chạy-local)
12. [Environment Variables](#12-environment-variables)
13. [CI/CD](#13-cicd)
14. [Trạng thái & Checklist](#14-trạng-thái--checklist)

---

## 1. Tư duy thiết kế

### Vấn đề cần giải quyết

HIS của bệnh viện (và các hệ thống bên ngoài) thường có nhiều **loại tài liệu** khác nhau trong cùng một hệ thống:

```
HIS Bệnh viện A gửi lên:
├── Hồ sơ bệnh nhân   → { patient_id, name, dob, department, ... }
├── Chứng từ viện phí → { invoice_id, patient_id, amount, date, items, ... }
├── Đơn thuốc         → { prescription_id, drug_name, quantity, doctor, ... }
└── Nhân viên         → { staff_id, name, role, department, ... }
```

Mỗi loại có **bộ field khác nhau**, và mỗi hệ thống lại **đặt tên field theo chuẩn riêng** (ví dụ: `patient_id` vs `ma_benh_nhan` vs `patientCode`). Điều này gây ra vấn đề khi muốn tổng hợp báo cáo từ nhiều nguồn.

### Giải pháp: SourceProfile + RecordType

**Ý tưởng cốt lõi:** Trước khi ingest, khai báo một "bản dịch" cho từng loại tài liệu từng nguồn. Bản dịch này gọi là **SourceProfile**.

```
SourceProfile = (SourceSystem, RecordType) → bảng mapping field
```

- `SourceSystem`: mã nguồn dữ liệu, ví dụ `"his-01"`, `"bhyt-hn"`
- `RecordType`: loại tài liệu trong nguồn đó, ví dụ `"benh-nhan"`, `"chung-tu"`
- Cặp `(SourceSystem, RecordType)` là **duy nhất** — mỗi loại tài liệu có mapping riêng

**Kết quả:** Dù HIS-A gọi là `patient_id` hay HIS-B gọi là `ma_benh_nhan`, sau khi ingest cả hai đều được lưu với tên chuẩn `MaBenhNhan`. Báo cáo chỉ cần đọc một tên field duy nhất.

```
HIS-A: { "patient_id": "BN-001" }  ──mapping──► { "MaBenhNhan": "BN-001" }
HIS-B: { "ma_benh_nhan": "BN-001" } ──mapping──► { "MaBenhNhan": "BN-001" }
                                                        ↑
                                               CanonicalPayload — báo cáo đọc cái này
```

---

## 2. Luồng hoạt động

```
[Bước 0 — Làm 1 lần]
POST /dm/sources  →  Đăng ký SourceProfile (mapping rules cho từng loại tài liệu)

[Bước 1 — Lặp lại mỗi khi có dữ liệu mới]
POST /dm/ingest/json  hoặc  POST /dm/ingest/file
        │
        ├─ Tra SourceProfile theo (sourceSystem, recordType)
        ├─ ApplyMappings: đổi tên field gốc → tên chuẩn   → CanonicalPayload
        ├─ SHA-256(RawPayload) → check trùng → 409 nếu đã có
        └─ INSERT StagingRecord (Status = Pending) vào PostgreSQL

[Bước 2 — Tự động, mỗi 30 giây]
MatchingWorker (background)
        └─ SELECT * WHERE Status = Pending LIMIT 50
           → MarkMatched("his-01::BN-001")
           → UPDATE Status = Matched

[Bước 3 — Lấy dữ liệu ra]
GET /dm/records     →  Tìm kiếm record theo sourceSystem, recordType, field bất kỳ
GET /dm/reports/{code}  →  Báo cáo aggregate (tổng hợp, nhóm theo khoa, ...)
```

---

## 3. Domain Model

### StagingRecord — vòng đời một record

```
Receive()  →  Pending
                │
                ▼ (MatchingWorker chọn)
            Processing
                │
        ┌───────┼──────────┐
        ▼       ▼          ▼
     Matched  Duplicate  Failed
  (key gán)  (hash đã    (lý do lưu ở
              tồn tại)    FailureReason)
```

| Field | Kiểu | Ý nghĩa |
|-------|------|---------|
| `Id` | Guid | Định danh duy nhất của record |
| `SourceSystem` | string | Mã nguồn, ví dụ `"his-01"` |
| `RecordType` | string | Loại tài liệu, ví dụ `"benh-nhan"` |
| `RawPayload` | text | JSON gốc từ nguồn — **không bao giờ thay đổi**, dùng để audit |
| `CanonicalPayload` | jsonb | JSON sau khi áp mapping — báo cáo và search đọc từ đây |
| `BusinessKey` | string | Khóa nghiệp vụ trích từ CanonicalPayload, ví dụ `"BN-001"` |
| `PayloadHash` | string | SHA-256 của RawPayload — dùng để phát hiện trùng lặp |
| `Status` | enum | Pending / Processing / Matched / Duplicate / Failed |
| `MatchedKey` | string | `"{SourceSystem}::{BusinessKey}"` sau khi matched |
| `FailureReason` | string | Lý do thất bại nếu Status = Failed |
| `ReceivedAt` | DateTime | Thời điểm nhận record |
| `ProcessedAt` | DateTime? | Thời điểm MatchingWorker xử lý xong |

### SourceProfile — cấu hình mapping

| Field | Ý nghĩa |
|-------|---------|
| `SourceSystem` | Mã nguồn, ví dụ `"his-01"` |
| `RecordType` | Loại tài liệu, ví dụ `"benh-nhan"` |
| `DisplayName` | Tên hiển thị, ví dụ `"HIS Bệnh viện A — Bệnh nhân"` |
| `BusinessKeyField` | Tên field canonical dùng làm khóa nghiệp vụ, ví dụ `"MaBenhNhan"` |
| `FieldMappingsJson` | JSON lưu bảng mapping: `{"tên_gốc": "tên_chuẩn", ...}` |

**Quy tắc quan trọng:** `businessKeyField` phải là một trong các **value** (không phải key) của `mappings`. Ví dụ nếu mappings có `{"patient_id": "MaBenhNhan"}` thì businessKeyField phải là `"MaBenhNhan"`.

> **Field không có trong mapping sẽ được giữ nguyên tên gốc** trong `CanonicalPayload`. Ví dụ: nếu payload gửi lên có field `extra_notes` nhưng mapping không khai báo, `CanonicalPayload` vẫn lưu `"extra_notes": "..."` (không bị bỏ qua).

---

## 4. API Reference

### `POST /dm/sources` — Đăng ký SourceProfile

Đăng ký mapping cho một loại tài liệu từ một nguồn. Phải làm **trước khi ingest**.

**Request:**
```json
{
  "sourceSystem":     "his-01",
  "recordType":       "benh-nhan",
  "displayName":      "HIS Bệnh viện A — Bệnh nhân",
  "businessKeyField": "MaBenhNhan",
  "mappings": {
    "patient_id":  "MaBenhNhan",
    "full_name":   "HoTen",
    "birth_date":  "NgaySinh",
    "department":  "TenKhoa",
    "status":      "TrangThai"
  }
}
```

| Field | Bắt buộc | Mô tả |
|-------|----------|-------|
| `sourceSystem` | ✅ | Mã nguồn, tối đa 100 ký tự |
| `recordType` | ✅ | Loại tài liệu, tối đa 100 ký tự |
| `displayName` | ✅ | Tên hiển thị, tối đa 200 ký tự |
| `businessKeyField` | ✅ | Phải là một value trong mappings |
| `mappings` | ✅ | Dict `{tên_gốc: tên_chuẩn}`, không được rỗng |

**Response `201 Created`:**
```json
{
  "success": true,
  "data": {
    "id": "3f2a...",
    "sourceSystem": "his-01",
    "recordType": "benh-nhan",
    "displayName": "HIS Bệnh viện A — Bệnh nhân",
    "businessKeyField": "MaBenhNhan",
    "mappings": { "patient_id": "MaBenhNhan", ... }
  }
}
```

**`409 Conflict`** nếu `(sourceSystem, recordType)` đã tồn tại.

---

### `GET /dm/sources` — Danh sách SourceProfile

```
GET /dm/sources                          → tất cả sources
GET /dm/sources?sourceSystem=his-01      → chỉ các loại của his-01
```

**Response `200 OK`:**
```json
{
  "success": true,
  "data": [
    { "sourceSystem": "his-01", "recordType": "benh-nhan", ... },
    { "sourceSystem": "his-01", "recordType": "chung-tu",  ... }
  ]
}
```

---

### `POST /dm/ingest/json` — Nạp 1 bản ghi JSON

**Request:**
```json
{
  "sourceSystem": "his-01",
  "recordType":   "benh-nhan",
  "payload": {
    "patient_id": "BN-001",
    "full_name":  "Nguyen Van A",
    "department": "Tim Mach",
    "status":     "Xuat vien"
  },
  "businessKeyOverride": null
}
```

| Field | Bắt buộc | Mô tả |
|-------|----------|-------|
| `sourceSystem` | ✅ | Phải khớp với SourceProfile đã đăng ký |
| `recordType` | ✅ | Phải khớp với SourceProfile đã đăng ký |
| `payload` | ✅ | JSON object bất kỳ |
| `businessKeyOverride` | ❌ | Tự truyền business key thay vì để hệ thống tự trích |

**Response `202 Accepted`:**
```json
{
  "success": true,
  "data": {
    "id":           "7816cf83-...",
    "sourceSystem": "his-01",
    "recordType":   "benh-nhan",
    "businessKey":  "BN-001",
    "status":       "Pending"
  }
}
```

**`409 Conflict`** nếu payload đã tồn tại (hash trùng).  
**`404 Not Found`** nếu SourceProfile chưa được đăng ký.

---

### `POST /dm/ingest/file` — Nạp batch từ file

Upload file JSON hoặc CSV chứa nhiều records cùng loại.

```
POST /dm/ingest/file
Content-Type: multipart/form-data

sourceSystem=his-01
recordType=benh-nhan
file=@patients.json
businessKeyOverride=BN-MANUAL-001   ← optional, áp dụng cho toàn batch
```

Giới hạn: **50 MB**. Record trùng (hash đã có) sẽ bị bỏ qua, không lỗi.

**Định dạng JSON:** array hoặc single object:
```json
[
  { "patient_id": "BN-001", "department": "Tim Mach", ... },
  { "patient_id": "BN-002", "department": "Noi Tiet", ... }
]
```

**Định dạng CSV:** dòng đầu là header, các dòng sau là data:
```csv
patient_id,full_name,department,status
BN-001,Nguyen Van A,Tim Mach,Xuat vien
BN-002,Tran Thi B,Noi Tiet,Dang dieu tri
```

> **Lưu ý:** CSV parser không xử lý quoted fields có dấu phẩy bên trong — dùng JSON cho dữ liệu phức tạp.

**Response `202 Accepted`:**
```json
{
  "success": true,
  "data": {
    "count": 2,
    "ids": ["7816cf83-...", "9a2b1c4d-..."]
  }
}
```

---

### `GET /dm/records` — Tìm kiếm record

Endpoint linh hoạt để lấy danh sách record theo bộ lọc bất kỳ. Chỉ trả về record có `Status = Matched`.

```
GET /dm/records?sourceSystem=his-01&recordType=benh-nhan
GET /dm/records?sourceSystem=his-01&recordType=benh-nhan&field=TenKhoa&value=Tim+Mach
GET /dm/records?recordType=chung-tu&from=2026-01-01&to=2026-03-31&limit=100
```

| Query param | Kiểu | Mô tả |
|-------------|------|-------|
| `sourceSystem` | string? | Lọc theo nguồn |
| `recordType` | string? | Lọc theo loại tài liệu |
| `field` | string? | Tên field trong CanonicalPayload cần lọc |
| `value` | string? | Giá trị cần tìm — **exact match, phân biệt hoa thường** (`@>` operator). `value=Tim Mach` ✅ `value=tim mach` ❌ |
| `from` | DateTime? | Từ ngày (ReceivedAt ≥ from) |
| `to` | DateTime? | Đến ngày (ReceivedAt ≤ to) |
| `limit` | int | Số record tối đa trả về (mặc định 200, tối đa 1000) |

**Response `200 OK`:**
```json
{
  "success": true,
  "data": [
    {
      "id":               "7816cf83-...",
      "sourceSystem":     "his-01",
      "recordType":       "benh-nhan",
      "businessKey":      "BN-001",
      "status":           "Matched",
      "canonicalPayload": "{\"MaBenhNhan\":\"BN-001\",\"HoTen\":\"Nguyen Van A\",\"TenKhoa\":\"Tim Mach\"}",
      "receivedAt":       "2026-05-27T10:00:00Z",
      "processedAt":      "2026-05-27T10:00:30Z"
    }
  ]
}
```

> `canonicalPayload` là JSON string — client tự parse để lấy các field cần thiết.

---

### `GET /dm/reports/{reportCode}` — Báo cáo aggregate

Chỉ tính trên record `Status = Matched`.

```
GET /dm/reports/chi-phi-theo-khoa?sourceSystem=his-01&recordType=chung-tu
GET /dm/reports/benh-nhan-theo-khoa?from=2026-01-01&to=2026-06-30
GET /dm/reports/tong-hop-nguon
```

| Query param | Mô tả |
|-------------|-------|
| `sourceSystem` | Lọc theo nguồn (optional) |
| `recordType` | Lọc theo loại tài liệu (optional) |
| `from` | Từ ngày (optional) |
| `to` | Đến ngày (optional) |

**Các report code hỗ trợ:**

| Code | Tên | Nhóm theo | Fields canonical cần có |
|------|-----|-----------|--------------------------|
| `chi-phi-theo-khoa` | Chi phí theo khoa | `TenKhoa` → SUM(`TongChiPhi`) | `TenKhoa`, `TongChiPhi` |
| `benh-nhan-theo-khoa` | Bệnh nhân theo khoa | `TenKhoa` × `TrangThai` | `TenKhoa`, `TrangThai` |
| `tong-hop-nguon` | Tổng hợp theo nguồn | `SourceSystem` | `TongChiPhi` (optional) |

**Response `200 OK`:**
```json
{
  "success": true,
  "data": {
    "reportCode":  "chi-phi-theo-khoa",
    "reportName":  "Chi phi theo khoa",
    "generatedAt": "2026-05-27T10:30:00Z",
    "columns": [
      { "key": "TenKhoa",    "label": "Ten khoa",    "type": "string" },
      { "key": "SoBenhNhan", "label": "So benh nhan","type": "number" },
      { "key": "TongChiPhi", "label": "Tong chi phi","type": "currency" }
    ],
    "rows": [
      { "data": { "TenKhoa": "Tim Mach", "SoBenhNhan": 10, "TongChiPhi": 15000000 } },
      { "data": { "TenKhoa": "Noi Tiet", "SoBenhNhan": 7,  "TongChiPhi": 8400000  } }
    ],
    "summary": {
      "TotalRecords": 17,
      "TotalChiPhi":  23400000
    }
  }
}
```

---

## 5. Hướng dẫn sử dụng từ đầu đến cuối

Ví dụ thực tế: HIS bệnh viện có 2 loại dữ liệu — **bệnh nhân** và **chứng từ viện phí**.

### Bước 1 — Đăng ký SourceProfile cho từng loại

```bash
# --- Loại 1: Bệnh nhân ---
curl -s -X POST http://localhost:5000/dm/sources \
  -H "Content-Type: application/json" \
  -d '{
    "sourceSystem":     "his-01",
    "recordType":       "benh-nhan",
    "displayName":      "HIS BV A — Ho so benh nhan",
    "businessKeyField": "MaBenhNhan",
    "mappings": {
      "patient_id":  "MaBenhNhan",
      "full_name":   "HoTen",
      "birth_date":  "NgaySinh",
      "department":  "TenKhoa",
      "status":      "TrangThai"
    }
  }' | python3 -m json.tool

# --- Loại 2: Chứng từ viện phí ---
curl -s -X POST http://localhost:5000/dm/sources \
  -H "Content-Type: application/json" \
  -d '{
    "sourceSystem":     "his-01",
    "recordType":       "chung-tu",
    "displayName":      "HIS BV A — Chung tu vien phi",
    "businessKeyField": "SoChungTu",
    "mappings": {
      "invoice_id":  "SoChungTu",
      "patient_id":  "MaBenhNhan",
      "department":  "TenKhoa",
      "total_cost":  "TongChiPhi",
      "invoice_date":"NgayLap",
      "status":      "TrangThai"
    }
  }' | python3 -m json.tool
```

**Xem lại tất cả sources đã đăng ký:**
```bash
curl -s "http://localhost:5000/dm/sources" | python3 -m json.tool

# Chỉ xem sources của his-01
curl -s "http://localhost:5000/dm/sources?sourceSystem=his-01" | python3 -m json.tool
```

---

### Bước 2 — Nạp dữ liệu bệnh nhân

```bash
# Nạp 1 bệnh nhân
curl -s -X POST http://localhost:5000/dm/ingest/json \
  -H "Content-Type: application/json" \
  -d '{
    "sourceSystem": "his-01",
    "recordType":   "benh-nhan",
    "payload": {
      "patient_id":  "BN-001",
      "full_name":   "Nguyen Van A",
      "birth_date":  "1985-03-15",
      "department":  "Tim Mach",
      "status":      "Xuat vien"
    }
  }' | python3 -m json.tool

# Nạp batch từ file JSON
curl -s -X POST http://localhost:5000/dm/ingest/file \
  -F "sourceSystem=his-01" \
  -F "recordType=benh-nhan" \
  -F "file=@patients.json" | python3 -m json.tool
```

---

### Bước 3 — Nạp dữ liệu chứng từ

```bash
curl -s -X POST http://localhost:5000/dm/ingest/json \
  -H "Content-Type: application/json" \
  -d '{
    "sourceSystem": "his-01",
    "recordType":   "chung-tu",
    "payload": {
      "invoice_id":   "CT-2026-001",
      "patient_id":   "BN-001",
      "department":   "Tim Mach",
      "total_cost":   1500000,
      "invoice_date": "2026-05-15",
      "status":       "Da thanh toan"
    }
  }' | python3 -m json.tool
```

---

### Bước 4 — Đợi MatchingWorker (tối đa 30 giây)

MatchingWorker chạy ngầm, cứ 30 giây xử lý các record Pending → Matched. Sau 30s, record mới có thể query và đưa vào báo cáo.

Để test nhanh hơn, set `Matching__WorkerIntervalSeconds=5` trong docker-compose.

---

### Bước 5 — Tìm kiếm record

```bash
# Tất cả bệnh nhân của his-01
curl -s "http://localhost:5000/dm/records?sourceSystem=his-01&recordType=benh-nhan" \
  | python3 -m json.tool

# Bệnh nhân khoa Tim Mạch
curl -s "http://localhost:5000/dm/records?sourceSystem=his-01&recordType=benh-nhan&field=TenKhoa&value=Tim+Mach" \
  | python3 -m json.tool

# Tìm theo tên bệnh nhân — phải khớp chính xác (case-sensitive)
curl -s "http://localhost:5000/dm/records?recordType=benh-nhan&field=HoTen&value=Nguyen+Van+A" \
  | python3 -m json.tool

# Chứng từ trong tháng 5/2026
curl -s "http://localhost:5000/dm/records?sourceSystem=his-01&recordType=chung-tu&from=2026-05-01&to=2026-05-31" \
  | python3 -m json.tool

# Lấy 50 record mới nhất của chứng từ
curl -s "http://localhost:5000/dm/records?recordType=chung-tu&limit=50" \
  | python3 -m json.tool
```

---

### Bước 6 — Lấy báo cáo

```bash
# Chi phí theo khoa — chỉ tính trên chứng từ (không lẫn record bệnh nhân)
curl -s "http://localhost:5000/dm/reports/chi-phi-theo-khoa?sourceSystem=his-01&recordType=chung-tu" \
  | python3 -m json.tool

# Bệnh nhân theo khoa × trạng thái — tính trên hồ sơ bệnh nhân
curl -s "http://localhost:5000/dm/reports/benh-nhan-theo-khoa?sourceSystem=his-01&recordType=benh-nhan" \
  | python3 -m json.tool

# Tổng hợp theo nguồn — tất cả sources, không lọc theo loại
curl -s "http://localhost:5000/dm/reports/tong-hop-nguon" \
  | python3 -m json.tool

# Báo cáo theo khoảng thời gian
curl -s "http://localhost:5000/dm/reports/chi-phi-theo-khoa?recordType=chung-tu&from=2026-01-01&to=2026-06-30" \
  | python3 -m json.tool
```

---

### Bước 7 — Test dedup

Gửi lại đúng record BN-001 đã ingest → sẽ bị từ chối:

```bash
curl -s -X POST http://localhost:5000/dm/ingest/json \
  -H "Content-Type: application/json" \
  -d '{
    "sourceSystem": "his-01",
    "recordType":   "benh-nhan",
    "payload": {
      "patient_id":  "BN-001",
      "full_name":   "Nguyen Van A",
      "birth_date":  "1985-03-15",
      "department":  "Tim Mach",
      "status":      "Xuat vien"
    }
  }' | python3 -m json.tool
```

**Response `409 Conflict`:**
```json
{
  "success": false,
  "error": { "code": "Conflict", "message": "Duplicate payload: a record with this exact content already exists." }
}
```

---

### Ví dụ với nhiều nguồn (cross-source)

Hệ thống hỗ trợ nhiều nguồn song song. Ví dụ thêm HIS của bệnh viện B:

```bash
# Đăng ký his-02 — field names khác nhau, nhưng canonical names giống his-01
curl -s -X POST http://localhost:5000/dm/sources \
  -H "Content-Type: application/json" \
  -d '{
    "sourceSystem":     "his-02",
    "recordType":       "benh-nhan",
    "displayName":      "HIS BV B — Ho so benh nhan",
    "businessKeyField": "MaBenhNhan",
    "mappings": {
      "ma_benh_nhan": "MaBenhNhan",
      "ho_va_ten":    "HoTen",
      "ngay_sinh":    "NgaySinh",
      "ten_khoa":     "TenKhoa",
      "trang_thai":   "TrangThai"
    }
  }' | python3 -m json.tool

# Ingest từ his-02 — dùng tên field khác nhưng kết quả lưu vào canonical giống nhau
curl -s -X POST http://localhost:5000/dm/ingest/json \
  -H "Content-Type: application/json" \
  -d '{
    "sourceSystem": "his-02",
    "recordType":   "benh-nhan",
    "payload": {
      "ma_benh_nhan": "BN-101",
      "ho_va_ten":    "Le Thi C",
      "ten_khoa":     "Noi Tiet",
      "trang_thai":   "Dang dieu tri"
    }
  }' | python3 -m json.tool

# Báo cáo tổng hợp toàn bộ bệnh nhân từ tất cả các nguồn
curl -s "http://localhost:5000/dm/reports/benh-nhan-theo-khoa?recordType=benh-nhan" \
  | python3 -m json.tool
```

---

## 6. Deduplication — SHA-256

Mỗi record được hash toàn bộ `RawPayload` trước khi lưu:

```
SHA-256(UTF-8 bytes of RawPayload) → hex string 64 ký tự
```

Nếu hash đã tồn tại trong DB → trả về `409 Conflict`, record không được lưu.

**Lưu ý quan trọng:**
- Hash tính trên **raw payload gốc** — cùng nội dung dù từ nguồn khác vẫn bị reject
- Nếu muốn cho phép cùng nội dung từ nguồn khác nhau, đổi thành hash canonical payload và thêm sourceSystem vào hash input
- Index `IX_StagingRecords_PayloadHash` đảm bảo check O(log n)

---

## 7. MatchingWorker

Background service chạy trong cùng process với API, xử lý định kỳ:

```
Mỗi {WorkerIntervalSeconds} giây (mặc định 30):
  1. GetPendingBatchAsync(50) — lấy tối đa 50 record Pending, sắp xếp theo ReceivedAt
  2. Với mỗi record:
     a. MarkProcessing()
     b. Tạo matchedKey = "{SourceSystem}::{BusinessKey}"
        (nếu không có BusinessKey → dùng PayloadHash thay thế)
     c. MarkMatched(matchedKey)
  3. SaveChangesAsync() — lưu tất cả thay đổi 1 lần
```

**Config:**
```json
{ "Matching": { "WorkerIntervalSeconds": 30 } }
```

> **Hiện tại là stub** — tạo composite key và đánh dấu Matched, chưa thực hiện cross-source matching (golden record, liên kết bệnh nhân từ nhiều nguồn). Cần implement thêm tùy nghiệp vụ.

---

## 8. Database — PostgreSQL

Connection string key: `DataMatchingDb`. Container riêng `postgres-dm`, không dùng chung SQL Server với các service khác.

### Cấu trúc bảng

```
postgres-dm (PostgreSQL 16)
  └── Database: DataMatchingDb
      ├── SourceProfiles
      │     UNIQUE INDEX: (SourceSystem, RecordType)
      │
      ├── StagingRecords
      │     INDEX: PayloadHash
      │     INDEX: (SourceSystem, RecordType)
      │     INDEX: Status
      │     INDEX: ReceivedAt
      │
      ├── OutboxMessage   ← MassTransit EF Outbox
      ├── OutboxState
      └── InboxState
```

Migration tự apply khi service khởi động (retry 10 lần, delay 3 giây).

### Kiểm tra DB trực tiếp

```bash
# Kết nối vào PostgreSQL local
docker exec -it hdos-postgres-dm psql -U dm_user -d DataMatchingDb

# Xem tất cả SourceProfiles
SELECT "SourceSystem", "RecordType", "DisplayName" FROM "SourceProfiles";

# Xem phân bố records theo trạng thái
SELECT "SourceSystem", "RecordType", "Status", COUNT(*)
FROM "StagingRecords"
GROUP BY "SourceSystem", "RecordType", "Status"
ORDER BY 1, 2, 3;

# Xem 10 record mới nhất
SELECT "SourceSystem", "RecordType", "BusinessKey", "Status", "ReceivedAt"
FROM "StagingRecords"
ORDER BY "ReceivedAt" DESC
LIMIT 10;

# Xem canonical payload của 1 record cụ thể
SELECT "CanonicalPayload"
FROM "StagingRecords"
WHERE "BusinessKey" = 'BN-001'
  AND "RecordType" = 'benh-nhan';
```

---

## 9. Performance & Scalability

### Tại sao cần jsonb + GIN index?

`CanonicalPayload` lưu JSON của từng record — đây là cột được filter nhiều nhất khi dùng `GET /dm/records?field=TenKhoa&value=Tim+Mach`.

**Trước khi có jsonb (cột `text`):**

```
Request: GET /dm/records?field=TenKhoa&value=Tim+Mach

1. PostgreSQL: SELECT * WHERE SourceSystem='his-01' AND RecordType='benh-nhan' LIMIT 2000
2. Application: Load 2000 rows lên RAM
3. Application: Parse JSON từng row, kiểm tra TenKhoa == "Tim Mach"
4. Trả về 200 kết quả

→ Với 1 triệu records: load hàng MB JSON, parse trong RAM → chậm, tốn memory
```

**Sau khi có jsonb + GIN index:**

```
Request: GET /dm/records?field=TenKhoa&value=Tim+Mach

1. PostgreSQL: SELECT * WHERE CanonicalPayload @> '{"TenKhoa":"Tim Mach"}'
              → dùng GIN index → O(log n) → trả về đúng rows cần
2. Application: Nhận kết quả sẵn, không cần parse thêm

→ Với 1 triệu records: ~5ms thay vì ~500ms
```

---

### jsonb là gì?

PostgreSQL có 2 kiểu lưu JSON:

| Kiểu | Lưu như | Có thể index? | Khi nào dùng |
|------|---------|---------------|--------------|
| `json` | Text nguyên văn | Không | Chỉ lưu, ít query |
| `jsonb` | Binary đã parse | **Có (GIN)** | Query theo field, filter |

`jsonb` tốn thêm ~20% disk (vì đã parse sẵn) nhưng query nhanh hơn nhiều.

---

### GIN index là gì?

GIN = **Generalized Inverted Index** — kiểu index dành riêng cho dữ liệu có nhiều key (JSON, array, full-text).

Cách hoạt động đơn giản:

```
GIN index của CanonicalPayload:

  "TenKhoa:Tim Mach"    → [row 1, row 2, row 5]
  "TenKhoa:Noi Tiet"    → [row 3, row 4]
  "TrangThai:Xuat vien" → [row 1, row 3]
  "MaBenhNhan:BN-001"   → [row 1]
  ...
```

Khi query `{"TenKhoa":"Tim Mach"}`:
- Nhìn vào bảng index → tìm key `"TenKhoa:Tim Mach"` → lấy `[row 1, row 2, row 5]` ngay
- Không cần đọc cả bảng

---

### Cách GIN index được tạo

```sql
-- Migration: AddJsonbAndGinIndex
-- 1. Đổi kiểu cột — USING bắt buộc khi data đã tồn tại
ALTER TABLE "StagingRecords"
ALTER COLUMN "CanonicalPayload" TYPE jsonb
USING "CanonicalPayload"::jsonb;

-- 2. Tạo GIN index với jsonb_path_ops
--    jsonb_path_ops: chỉ hỗ trợ @> nhưng nhỏ hơn và nhanh hơn jsonb_ops mặc định
CREATE INDEX "IX_StagingRecords_CanonicalPayload_gin"
ON "StagingRecords"
USING GIN ("CanonicalPayload" jsonb_path_ops);
```

---

### Toán tử @> (containment)

Khi gọi `GET /dm/records?field=TenKhoa&value=Tim+Mach`, service tạo query:

```sql
-- EF Core dịch EF.Functions.JsonContains(...) thành:
SELECT * FROM "StagingRecords"
WHERE "Status" = 'Matched'
  AND "SourceSystem" = 'his-01'
  AND "RecordType" = 'benh-nhan'
  AND "CanonicalPayload" @> '{"TenKhoa":"Tim Mach"}'::jsonb
ORDER BY "ReceivedAt" DESC
LIMIT 200;
```

`@>` = "chứa". `{"TenKhoa":"Tim Mach"}` có phải là subset của CanonicalPayload không?

```json
CanonicalPayload: {"MaBenhNhan":"BN-001","HoTen":"Nguyen Van A","TenKhoa":"Tim Mach","TrangThai":"Xuat vien"}
Filter:          {"TenKhoa":"Tim Mach"}

→ ✅ Kết quả: match (filter là subset của canonical)
```

**Lưu ý quan trọng:** `@>` là **exact match, case-sensitive**.
- `value=Tim Mach` → tìm được ✅
- `value=tim mach` → không tìm được ❌
- `value=Tim` → không tìm được ❌ (không phải partial match)

---

### Xác nhận GIN index hoạt động với EXPLAIN ANALYZE

```bash
docker exec hdos-postgres-dm psql -U dm_user -d DataMatchingDb -c \
"SET enable_seqscan = off;
 EXPLAIN ANALYZE
 SELECT * FROM \"StagingRecords\"
 WHERE \"CanonicalPayload\" @> '{\"TenKhoa\":\"Tim Mach\"}'::jsonb
 LIMIT 10;"
```

Output khi data đủ lớn:
```
Bitmap Heap Scan on "StagingRecords"
  ->  Bitmap Index Scan on "IX_StagingRecords_CanonicalPayload_gin"
        Index Cond: ("CanonicalPayload" @> '{"TenKhoa": "Tim Mach"}'::jsonb)
```

> **Lưu ý:** Khi table còn nhỏ (< vài nghìn rows), PostgreSQL tự chọn Seq Scan vì rẻ hơn. GIN index sẽ được dùng tự động khi data lớn — đây là hành vi đúng của query planner.

---

### Capacity planning

| Số records | Storage (ước tính) | Query với GIN | Query không có GIN |
|-----------|---------------------|---------------|---------------------|
| 100K | ~500 MB | < 5ms | ~50ms |
| 1M | ~5 GB | < 10ms | ~500ms |
| 10M | ~50 GB | < 30ms | ~5s |
| 100M | ~500 GB | < 100ms | Timeout |

PostgreSQL xử lý tốt đến ~50-100M rows với index đúng. Trên đó cần partition.

---

### Khi nào cần nâng cấp thêm?

**> 5 triệu rows** — Thêm partitioning theo tháng:

```sql
-- Chia bảng theo ReceivedAt, mỗi partition là 1 tháng
-- Query tự động chỉ scan partition liên quan
CREATE TABLE "StagingRecords_2026_05"
PARTITION OF "StagingRecords"
FOR VALUES FROM ('2026-05-01') TO ('2026-06-01');
```

**> 50 triệu rows** — Tách bảng riêng theo `(SourceSystem, RecordType)` nếu mỗi loại có hàng chục triệu records.

---

## 10. Kiến trúc nội bộ

```
Domain/
  Entities/
    StagingRecord     — AggregateRoot, state machine (Pending → Matched/Duplicate/Failed)
    SourceProfile     — lưu mapping rules, keyed by (SourceSystem, RecordType)
  Enums/
    RecordStatus      — Pending, Processing, Matched, Duplicate, Failed
  Repositories/
    IStagingRecordRepository   — CRUD + GetFilteredAsync + GetMatchedAsync
    ISourceProfileRepository   — GetBySystemAndTypeAsync + GetAllAsync
    IDataMatchingUnitOfWork    — SaveChangesAsync

Application/
  Features/
    Sources/    RegisterSourceCommand, GetSourcesQuery
    Ingest/     IngestJsonCommand, IngestFileCommand
    Records/    GetRecordsQuery   ← search linh hoạt theo field bất kỳ
    Reports/    GetReportQuery    ← 3 built-in aggregate reports
  DTOs/
    SourceProfileDto, IngestResultDto, IngestBatchResultDto,
    StagingRecordDto, ReportDto, ReportColumnDto, ReportRowDto

Infrastructure/
  Persistence/
    DataMatchingDbContext       — Npgsql + MassTransit EF Outbox tables
    SourceProfileRepository
    StagingRecordRepository    — filter bằng EF.Functions.JsonContains → SQL @> (GIN index)
    Configurations/            — EF Core fluent config cho cả 2 entities
    Migrations/                — InitialCreate + AddRecordType + AddJsonbAndGinIndex
  Workers/
    MatchingWorker             — IHostedService, batch 50, interval 30s
  DependencyInjection.cs       — UseNpgsql + UsePostgres (outbox)

API/
  Controllers/
    SourcesController          — POST/GET /dm/sources
    IngestController           — POST /dm/ingest/json, POST /dm/ingest/file
    RecordsController          — GET /dm/records
    ReportsController          — GET /dm/reports/{code}
  Program.cs                   — JWT, OTel, AddNpgSql health check, auto-migrate
```

---

## 11. Chạy local

### Docker Compose (khuyến nghị)

```bash
docker compose up -d
```

DataMatchingService và `postgres-dm` khởi động cùng stack.

**Swagger UI:** `http://localhost:5000/dm/swagger`

### dotnet run (debug / hot-reload)

```bash
# Khởi động dependencies trước
docker compose up -d postgres-dm rabbitmq

# Chạy service
cd src/Services/DataMatchingService/DataMatchingService.API
dotnet run
```

`appsettings.json` đã trỏ `Host=localhost;Port=5433` — đúng với port-forward của `postgres-dm`.

### Tạo migration mới

```bash
DOTNET_ROOT=~/.dotnet PATH="$HOME/.dotnet:$PATH" \
~/.dotnet/tools/dotnet-ef migrations add <TenMigration> \
  --project src/Services/DataMatchingService/DataMatchingService.Infrastructure \
  --startup-project src/Services/DataMatchingService/DataMatchingService.API \
  --output-dir Persistence/Migrations \
  --context DataMatchingDbContext
```

---

## 12. Environment Variables

| Biến | Ý nghĩa | Ví dụ |
|------|---------|-------|
| `ConnectionStrings__DataMatchingDb` | PostgreSQL connection string | `Host=postgres-dm;Port=5432;Database=DataMatchingDb;Username=dm_user;Password=...` |
| `Matching__WorkerIntervalSeconds` | Chu kỳ MatchingWorker (giây) | `30` |
| `RabbitMq__Host` | RabbitMQ host | `rabbitmq` |
| `RabbitMq__Port` | RabbitMQ port | `5672` |
| `Jwt__Secret` | JWT signing key (chia sẻ với AuthService) | — |
| `ASPNETCORE_ENVIRONMENT` | Môi trường | `Development` / `Production` |

---

## 13. CI/CD

Được tích hợp đầy đủ vào CI/CD pipeline (xem [doc 10](./10-cicd-pipeline.md)):

- **`services.json`**: entry `datamatchingservice` → Dockerfile path
- **`.github/path-filters.yml`**: rebuild khi `src/Services/DataMatchingService/**` thay đổi
- **`docker-compose.server.yml`**: pull image từ GHCR + `datamatchingservice.env` (optional) + `postgres-dm` (port đóng trên server)

**Setup server lần đầu:**

```bash
# 1. Thêm vào /opt/hdos-prod/.env
POSTGRES_DM_PASSWORD=<strong-password>

# 2. Tạo env file cho service
cat > /opt/hdos-prod/datamatchingservice.env << 'EOF'
ConnectionStrings__DataMatchingDb=Host=postgres-dm;Port=5432;Database=DataMatchingDb;Username=dm_user;Password=<password>
Matching__WorkerIntervalSeconds=30
EOF
```

> CD pipeline tự động tạo file trống nếu chưa có. Nhớ điền giá trị thực sau deploy lần đầu.

---

## 14. Trạng thái & Checklist

### Trạng thái hiện tại

| Phần | Trạng thái | Ghi chú |
|------|-----------|---------|
| Domain (StagingRecord, SourceProfile + RecordType) | ✅ | State machine đầy đủ, keyed by (SourceSystem, RecordType) |
| Ingest JSON + dedup SHA-256 | ✅ | Production-ready |
| Ingest File (JSON + CSV batch) | ✅ | Tối đa 50 MB |
| GET /dm/records (search linh hoạt) | ✅ | Filter theo sourceSystem, recordType, field bất kỳ, date range |
| 3 built-in reports | ✅ | chi-phi-theo-khoa, benh-nhan-theo-khoa, tong-hop-nguon |
| Lọc report theo recordType | ✅ | Không lẫn dữ liệu giữa các loại |
| PostgreSQL + EF migrations | ✅ | Auto-apply khi startup |
| `CanonicalPayload` kiểu `jsonb` | ✅ | Filter trực tiếp trong SQL, không in-memory |
| GIN index trên `CanonicalPayload` | ✅ | `jsonb_path_ops`, tối ưu cho `@>` containment |
| MassTransit Outbox (PostgreSQL) | ✅ Configured | Chưa có integration event nào được publish |
| Docker Compose (local) | ✅ | `postgres-dm` + `datamatchingservice` |
| CI/CD pipeline | ✅ | path-filter, GHCR build, server override |
| MatchingWorker | ⚠️ Stub | Tạo key, chưa match thực sự cross-source |
| `[Authorize]` trên controllers | ❌ Comment-out | Bật lại trước production |
| Tests | ❌ Chưa có | — |

### Checklist trước production

- [ ] Bỏ comment `// [Authorize]` trên `IngestController`, `SourcesController`, `ReportsController`, `RecordsController`
- [x] Tạo `datamatchingservice.env` trên server với PostgreSQL password thực
- [x] Thêm `POSTGRES_DM_PASSWORD` vào `/opt/hdos-prod/.env`
- [ ] Implement matching logic thực trong `MatchingWorker` (cross-source golden record)
- [ ] Thêm pagination (cursor-based) cho `GET /dm/records` khi data lớn
- [x] Chuyển CanonicalPayload sang `jsonb` + GIN index — filter bằng SQL `@>`, không in-memory
- [ ] Cập nhật bảng Outbox trong [doc 21](./21-outbox-pattern.md) khi thêm integration events
