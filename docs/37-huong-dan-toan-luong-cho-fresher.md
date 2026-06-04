# 37 — Hướng dẫn toàn luồng DataMatching → DynamicForm (cho Fresher)

> Tài liệu này giải thích **TỪ ĐẦU ĐẾN CUỐI** cách hoạt động của hai service quan trọng nhất
> trong hệ thống Hdos: **DataMatchingService** và **DynamicFormService**, cộng thêm cách chúng
> phối hợp với nhau qua Frontend.
>
> Đối tượng: lập trình viên fresher mới vào nghề. Không cần kiến thức trước về microservices.
> Mọi thuật ngữ kỹ thuật đều được giải thích.
>
> Toàn bộ ví dụ trong tài liệu này đều được **test thật trên live server** tại
> `https://192.168.100.60:8443/` — bạn có thể copy curl và chạy lại sẽ ra đúng kết quả.

---

## Mục lục

0. [Glossary — Từ điển thuật ngữ](#0-glossary)
1. [Bài toán thực tế — Tại sao cần hệ thống này?](#1-bài-toán-thực-tế)
2. [Kiến trúc tổng thể](#2-kiến-trúc-tổng-thể)
3. [DataMatchingService — Đầy đủ chi tiết](#3-datamatchingservice)
4. [DynamicFormService — Đầy đủ chi tiết](#4-dynamicformservice)
5. [Expression Binding — Phép màu kết nối 2 service](#5-expression-binding)
6. [Full luồng từ A đến Z](#6-full-luồng)
7. [Widget và Dashboard cũng dùng được](#7-widget-và-dashboard)
8. [Tư duy thiết kế — Tại sao làm vậy không làm khác?](#8-tư-duy-thiết-kế)
9. [Checklist & Troubleshooting](#9-checklist--troubleshooting)
10. [Tóm tắt 1 trang](#10-tóm-tắt-1-trang)

---

## 0. Glossary

Trước khi đọc, hãy nắm 25 thuật ngữ này. Nếu đang đọc mà gặp từ lạ → quay lại đây tra.

| Từ | Giải thích đơn giản |
|---|---|
| **Microservice** | Một ứng dụng nhỏ chuyên 1 nghiệp vụ. Hệ thống Hdos có 7 microservice (AuthService, OrderService, DataMatchingService...). Mỗi service có DB riêng, deploy riêng. |
| **API** | Cách 2 chương trình "nói chuyện" với nhau qua mạng. Bên gọi là **client**, bên nhận là **server**. |
| **REST API** | Kiểu API phổ biến nhất: dùng HTTP method (GET, POST, PUT, DELETE) + URL + JSON body. Ví dụ `POST /dm/sources` = "tạo source profile". |
| **HTTP method** | GET = đọc dữ liệu. POST = tạo mới. PUT = update toàn bộ. PATCH = update một phần. DELETE = xóa. |
| **HTTP status code** | 200 = OK. 201 = Created. 202 = Accepted (sẽ xử lý sau). 400 = bạn gửi sai. 401 = chưa login. 403 = không có quyền. 404 = không tìm thấy. 409 = xung đột (trùng). 500 = server lỗi. |
| **JSON** | Cách viết dữ liệu dạng text dễ đọc: `{"hoTen": "An", "tuoi": 30}`. Tất cả API trong dự án này đều dùng JSON. |
| **JSONB** | Kiểu cột trong **PostgreSQL** lưu JSON dưới dạng binary (nén + có index). Search nhanh hơn TEXT 10×. Hdos dùng cho `CanonicalPayload`, `DataBindingJson`... |
| **GIN index** | Loại index của PostgreSQL chuyên cho dữ liệu phi cấu trúc (JSONB, array, full-text). Cho phép query `WHERE jsonb_col @> '{"key":"value"}'` cực nhanh. |
| **EF Core** | **Entity Framework Core** — thư viện ORM của .NET. Cho phép viết code C# `dbContext.Users.Where(u => u.Email == "x").ToList()` thay vì viết SQL. |
| **Migration** | File C# do EF Core sinh ra mô tả thay đổi schema DB (thêm bảng, thêm cột...). Chạy `dotnet ef database update` để apply lên DB thật. |
| **Aggregate Root** | Khái niệm DDD (Domain-Driven Design): một entity "lãnh đạo" một nhóm entity con. Ngoài hệ thống chỉ được truy cập aggregate root, không động vào con trực tiếp. Ví dụ: `FormTemplate` là aggregate root quản `FormField` con. |
| **Value Object** | Object immutable (không sửa được), so sánh bằng giá trị chứ không bằng ID. Ví dụ `DataBinding("expression", "format")` — 2 instance cùng giá trị thì coi là bằng nhau. |
| **CQRS** | **Command Query Responsibility Segregation** — tách 2 loại request: **Command** (sửa data, vd `CreateOrder`) và **Query** (đọc data, vd `GetOrderById`). Tách giúp dễ optimize, dễ test. |
| **MediatR** | Thư viện .NET giúp implement CQRS. Controller chỉ cần `sender.Send(command)` — MediatR tự tìm `Handler` xử lý. Giảm coupling. |
| **Handler** | Class xử lý 1 Command/Query. Mỗi Command có đúng 1 Handler. Pattern: `XxxCommand` + `XxxCommandHandler`. |
| **FluentValidation** | Thư viện validate input theo style fluent: `RuleFor(x => x.Email).NotEmpty().EmailAddress()`. Mỗi Command có 1 Validator. |
| **DTO** | **Data Transfer Object** — object dùng để truyền dữ liệu giữa các layer hoặc qua mạng. Khác với Entity (object DB). DTO thường là `record`. |
| **Result\<T\>** | Pattern thay cho exception. Handler trả `Result.Success(value)` hoặc `Result.Failure(Error.NotFound(...))`. Caller check `result.IsSuccess`. Không throw để biểu diễn lỗi nghiệp vụ. |
| **SHA-256** | Hàm hash mật mã: input bất kỳ → 64 ký tự hex cố định. Cùng input → cùng output. Đổi 1 ký tự → output đổi hoàn toàn. Dùng để chống trùng. |
| **Mustache** | Cú pháp template `{{biến}}` đơn giản. Hdos dùng cho expression binding: `{{sources.record.HoTen}}`. |
| **gRPC** | Cách 2 service gọi nhau **đồng bộ** qua HTTP/2 + Protobuf. Nhanh hơn REST. Hdos dùng cho `OrderService → AuthService:8081`. |
| **RabbitMQ** | Message broker — service A gửi message vào "ống", service B lấy ra xử lý. **Bất đồng bộ** = A không cần chờ B. Hdos dùng cho `OrderCreatedIntegrationEvent`. |
| **MassTransit** | Thư viện .NET wrap RabbitMQ. Code đẹp hơn, có Outbox pattern (đảm bảo at-least-once). |
| **JWT** | **JSON Web Token** — chuỗi base64 chứa user info + chữ ký. Login xong nhận JWT, mọi request sau gắn header `Authorization: Bearer <jwt>`. |
| **SDUI** | **Server-Driven UI** — server trả về JSON mô tả "vẽ cái gì ở đâu", frontend chỉ render. DynamicFormService dùng SDUI cho screen layout. |
| **BDUI** | **Backend-Driven UI** — gần giống SDUI nhưng cụ thể là server trả schema form (field nào, kiểu gì), frontend render form theo. Endpoint `/forms/{module}/{key}/schema` là BDUI. |
| **Background Service** | Service chạy ngầm trong .NET process, không phụ thuộc HTTP request. Ví dụ `MatchingWorker` chạy mỗi 30 giây tự động. |

---

## 1. Bài toán thực tế

### 1.1 Tưởng tượng bạn là bác sĩ tại bệnh viện

Bạn cần xem thông tin bệnh nhân **Phạm Quỳnh Như** đang nằm tại Khoa Tim Mạch để điền phiếu xét duyệt.
Thông tin bệnh nhân nằm trong hệ thống **HIS** (Hospital Information System) của bệnh viện.
Bạn cần điền thêm:
- **Kết luận xét duyệt** (Đạt / Cần bổ sung / Không đạt)
- **Ghi chú** của bác sĩ

### 1.2 Vấn đề kỹ thuật

Một bệnh viện có thể dùng phần mềm HIS từ rất nhiều hãng khác nhau:

```
Bệnh viện A dùng HIS-X:
{ "patient_name": "Nguyen Van An", "dept": "Cardiology" }

Bệnh viện B dùng HIS-Y:
{ "TenBN": "Trần Thị Bình", "Khoa": "Nhi Khoa" }
```

Cả 2 cùng là thông tin bệnh nhân nhưng **tên field hoàn toàn khác nhau**.
Giao diện form cũng khác tùy bệnh viện: A muốn 1 cột, B muốn 2 cột, C muốn thêm field "Dị ứng thuốc"...

**Nếu code cứng:** mỗi bệnh viện = 1 dự án riêng. Bệnh viện C thêm field → dev phải code → deploy → cập nhật.
Chi phí cao, chậm, dễ sai.

### 1.3 Giải pháp: tách thành 2 service độc lập

```
┌────────────────────────────────────────────────────────────────┐
│                                                                │
│   Phần 1: DataMatchingService                                  │
│   ────────────────────────────                                 │
│   • Nhận data từ BẤT KỲ nguồn nào (HIS-A, HIS-B, Excel, API...)│
│   • Chuẩn hóa tên field về một format thống nhất               │
│   • Chống trùng lặp (SHA-256 hash)                             │
│   • Lưu lại để truy xuất bất kỳ lúc nào                        │
│                                                                │
│   Phần 2: DynamicFormService                                   │
│   ──────────────────────                                       │
│   • Admin tạo form qua API (không cần code lại frontend)       │
│   • Form tự "kéo" dữ liệu từ DataMatching qua expression       │
│   • Bác sĩ chỉ cần điền phần ý kiến của mình                   │
│   • Submission lưu lại để audit                                │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

Hai service **HOÀN TOÀN KHÔNG BIẾT VỀ NHAU** — frontend là cầu nối duy nhất. Lý do sẽ giải thích kỹ ở phần 8.1.

---

## 2. Kiến trúc tổng thể

### 2.1 Sơ đồ tổng thể

```
┌──────────────────────────────────────────────────────────────────────┐
│                                                                      │
│  ┌─────────────────────┐         ┌─────────────────────────┐         │
│  │ DataMatchingService │         │  DynamicFormService     │         │
│  │                     │         │                         │         │
│  │ ┌─────────────────┐ │         │ ┌─────────────────────┐ │         │
│  │ │  REST API       │ │         │ │  REST API           │ │         │
│  │ │  (Controllers)  │ │         │ │  (Controllers)      │ │         │
│  │ ├─────────────────┤ │         │ ├─────────────────────┤ │         │
│  │ │  Application    │ │         │ │  Application        │ │         │
│  │ │  (CQRS + MediatR)│         │ │  (CQRS + MediatR)   │ │         │
│  │ ├─────────────────┤ │         │ ├─────────────────────┤ │         │
│  │ │  Domain         │ │         │ │  Domain             │ │         │
│  │ │  (Entities,VO)  │ │         │ │  (Entities, VO)     │ │         │
│  │ ├─────────────────┤ │         │ ├─────────────────────┤ │         │
│  │ │  Infrastructure │ │         │ │  Infrastructure     │ │         │
│  │ │  (EF Core,Worker)│         │ │  (EF Core)          │ │         │
│  │ └────────┬────────┘ │         │ └──────────┬──────────┘ │         │
│  │          │          │         │            │            │         │
│  │  PostgreSQL          │         │     PostgreSQL          │         │
│  │  (DataMatchingDb)    │         │     (DynamicFormDb)     │         │
│  └──────────┬───────────┘         └────────────┬────────────┘         │
│             │                                  │                      │
│             │      ┌─────────────────┐         │                      │
│             └─────►│   Frontend      │◄────────┘                      │
│                    │   (Browser SPA) │                                │
│                    │                 │                                │
│                    │  - Fetch        │                                │
│                    │  - Evaluate     │                                │
│                    │  - Render       │                                │
│                    │  - Submit       │                                │
│                    └─────────────────┘                                │
│                            ▲                                          │
│                            │                                          │
│                       Bác sĩ / Admin                                  │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

### 2.2 Nguyên tắc quan trọng nhất: Zero Coupling

> **DataMatchingService và DynamicFormService KHÔNG gọi lẫn nhau.**
> Frontend là cầu nối duy nhất.

Đây là **nguyên tắc thiết kế CỐT LÕI**. Lý do:

- **Fault isolation**: DataMatching down → DynamicForm vẫn lên được (chỉ là không có data điền)
- **Scale độc lập**: DataMatching nhận hàng triệu record/ngày, cần 10 instance. DynamicForm vài chục request/giây, 2 instance đủ
- **Deploy độc lập**: Update DataMatching không cần restart DynamicForm
- **Test dễ**: Test DynamicForm không cần dựng DataMatching
- **Tương lai**: Mai có ServiceX cũng cung cấp data tương tự → DynamicForm chỉ cần đổi config, không sửa code

### 2.3 Clean Architecture — 4 tầng

Mở thư mục `src/Services/DynamicFormService/` bạn sẽ thấy 4 project:

```
DynamicFormService.Domain         ← Tầng trong cùng (lõi)
DynamicFormService.Application    ← Use case nghiệp vụ
DynamicFormService.Infrastructure ← Truy cập DB, message broker, file...
DynamicFormService.API            ← Tầng ngoài cùng (Controller, DI)
```

DataMatchingService cũng có 4 project tương tự. Đây là **Clean Architecture** — quy tắc:

> **Tầng ngoài biết tầng trong, tầng trong KHÔNG biết tầng ngoài.**

```
┌────────────────────────────────────────────┐
│  API (Controllers, DI, Startup)            │
│   ▼ phụ thuộc Application                  │
│  ┌──────────────────────────────────────┐  │
│  │  Application (Commands, Handlers)    │  │
│  │   ▼ phụ thuộc Domain                 │  │
│  │  ┌────────────────────────────────┐  │  │
│  │  │  Domain (Entities, VO, Enums)  │  │  │
│  │  │  ❌ KHÔNG phụ thuộc ai         │  │  │
│  │  └────────────────────────────────┘  │  │
│  └──────────────────────────────────────┘  │
│                                            │
│  Infrastructure (EF Core, Repos)           │
│   ▼ phụ thuộc Domain (implement interface) │
└────────────────────────────────────────────┘
```

**Vai trò từng tầng:**

| Tầng | Chứa gì | Phụ thuộc |
|---|---|---|
| **Domain** | Entity, Value Object, Enum, Interface Repository, Domain Event. **Code thuần C#, không có thư viện nào**. | Không phụ thuộc ai. |
| **Application** | Command, Query, Handler, Validator, DTO. Logic nghiệp vụ. | Phụ thuộc Domain. |
| **Infrastructure** | `DbContext`, `Repository` implementation, MassTransit consumer, gRPC client. | Phụ thuộc Domain + Application interface. |
| **API** | `Controller`, `Program.cs`, DI registration, middleware. | Phụ thuộc tất cả 3 tầng trên. |

**Tại sao tách 4 tầng?**

1. **Domain pure** → dễ test bằng unit test, không cần dựng DB
2. **Đổi DB từ Postgres sang SQL Server** → chỉ sửa Infrastructure, Domain/Application không động
3. **Đổi từ REST sang gRPC** → chỉ sửa API, các tầng dưới không động
4. **Logic nghiệp vụ tập trung 1 chỗ** (Domain + Application) → dễ tìm, dễ sửa, dễ review

---

## 3. DataMatchingService

### 3.1 Vấn đề nó giải quyết

> Nhận dữ liệu từ NHIỀU nguồn khác nhau (mỗi nguồn có tên field khác nhau), chuẩn hóa thành 1 format chung, chống trùng, lưu để truy xuất.

### 3.2 Sơ đồ ER (Entity Relationship)

DataMatchingService chỉ có **2 bảng chính** trong PostgreSQL:

```
┌─────────────────────────────────┐
│       SourceProfiles            │  ← "Bản đồ dịch field" do admin đăng ký 1 lần
├─────────────────────────────────┤
│ Id                 UUID         │
│ SourceSystem       VARCHAR(100) │ ─┐
│ RecordType         VARCHAR(100) │ ─┤  (SourceSystem, RecordType) UNIQUE
│ DisplayName        VARCHAR(200) │  │
│ BusinessKeyField   VARCHAR(200) │  │
│ FieldMappingsJson  TEXT         │  │   JSON: {"ten_goc": "TenChuan"}
│ CreatedAtUtc       TIMESTAMP    │  │
│ UpdatedAtUtc       TIMESTAMP    │  │
└─────────────────────────────────┘  │
                                     │  Lookup khi ingest:
                                     ▼  WHERE SourceSystem=? AND RecordType=?
┌─────────────────────────────────┐
│       StagingRecords            │  ← Từng record dữ liệu thực tế
├─────────────────────────────────┤
│ Id                 UUID         │
│ SourceSystem       VARCHAR(100) │ ─→ liên kết logic với SourceProfile
│ RecordType         VARCHAR(100) │ ─┘
│ RawPayload         TEXT         │   JSON gốc từ HIS gửi lên
│ CanonicalPayload   JSONB        │   JSON sau khi rename field
│ BusinessKey        VARCHAR(500) │   ID nghiệp vụ (MaBenhNhan...)
│ PayloadHash        VARCHAR(64)  │   SHA-256 của RawPayload (chống trùng)
│ Status             VARCHAR(30)  │   Pending|Processing|Matched|Duplicate|Failed
│ MatchedKey         VARCHAR(500) │   "sourceSystem::businessKey"
│ FailureReason      VARCHAR(1000)│   Lý do nếu Status=Failed
│ ReceivedAt         TIMESTAMP    │   Lúc nhận từ HIS
│ ProcessedAt        TIMESTAMP    │   Lúc Worker chuyển sang Matched
└─────────────────────────────────┘
   Indexes:
   - PayloadHash                 ← check trùng nhanh
   - (SourceSystem, RecordType)  ← filter phổ biến
   - Status                      ← Worker query Pending
   - ReceivedAt                  ← sort/range
   - CanonicalPayload (GIN)      ← search JSONB containment
```

**Lưu ý:** giữa 2 bảng KHÔNG có Foreign Key cứng. Chỉ liên kết logic qua `(SourceSystem, RecordType)`. Lý do: SourceProfile có thể bị xóa/sửa nhưng StagingRecord phải tồn tại độc lập.

### 3.3 SourceProfile — Bản đồ dịch field chi tiết

#### 3.3.1 Domain entity

```csharp
public sealed class SourceProfile : BaseEntity<Guid>
{
    public string SourceSystem      { get; private set; }   // "his-fresher"
    public string RecordType        { get; private set; }   // "benh-nhan"
    public string DisplayName       { get; private set; }   // "HIS Fresher Demo - BN"
    public string BusinessKeyField  { get; private set; }   // "MaBenhNhan"
    public string FieldMappingsJson { get; private set; }   // JSON

    public static SourceProfile Create(
        string sourceSystem, string recordType,
        string displayName, string businessKeyField,
        Dictionary<string, string> mappings) { ... }

    public Dictionary<string, string> GetMappings() { ... }
}
```

#### 3.3.2 Đăng ký một SourceProfile (test thật)

```bash
curl -X POST "https://192.168.100.60:8443/dm/sources" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceSystem": "his-fresher",
    "recordType": "benh-nhan",
    "displayName": "HIS Fresher Demo - Benh nhan",
    "businessKeyField": "MaBenhNhan",
    "mappings": {
      "ma_bn":     "MaBenhNhan",
      "ho_ten":    "HoTen",
      "ngay_sinh": "NgaySinh",
      "ten_khoa":  "TenKhoa",
      "so_giuong": "SoGiuong",
      "chan_doan": "ChanDoan",
      "bac_si":    "BacSiPhuTrach"
    }
  }'
```

**Response (HTTP 201 Created):**

```json
{
  "success": true,
  "data": {
    "id": "674b1b4f-4337-4689-9bf6-126e85bacf89",
    "sourceSystem": "his-fresher",
    "recordType": "benh-nhan",
    "displayName": "HIS Fresher Demo - Benh nhan",
    "businessKeyField": "MaBenhNhan",
    "mappings": {
      "ma_bn": "MaBenhNhan",
      "ho_ten": "HoTen",
      "ngay_sinh": "NgaySinh",
      "ten_khoa": "TenKhoa",
      "so_giuong": "SoGiuong",
      "chan_doan": "ChanDoan",
      "bac_si": "BacSiPhuTrach"
    }
  }
}
```

#### 3.3.3 Giải thích từng field

| Field | Ý nghĩa | Ràng buộc |
|---|---|---|
| `sourceSystem` | Mã định danh hệ thống nguồn. Lowercase, dùng dấu gạch ngang. | Bắt buộc, ≤100 ký tự. Unique cùng `recordType`. |
| `recordType` | Loại dữ liệu trong nguồn đó. 1 source có thể có nhiều type. | Bắt buộc, ≤100 ký tự. |
| `displayName` | Tên hiển thị cho người dùng admin xem. | Bắt buộc, ≤200 ký tự. |
| `businessKeyField` | Tên field **trong canonical** (sau khi map) làm "khóa nghiệp vụ". Dùng để build MatchedKey. | Bắt buộc. **Phải là VALUE trong `mappings`.** |
| `mappings` | Dictionary: `key = tên field gốc`, `value = tên chuẩn`. Worker dùng map này rename. | Bắt buộc, ≥1 mapping. |

**Lưu ý quan trọng về `businessKeyField`:**
Phải là tên SAU KHI map. Ví dụ trên: `"ma_bn": "MaBenhNhan"` → `businessKeyField` phải là `"MaBenhNhan"`, KHÔNG phải `"ma_bn"`.

### 3.4 Tất cả endpoint của DataMatchingService

Service mở trên route prefix `/dm`. Đầy đủ 6 controllers:

#### IngestController (`/dm/ingest`)

| HTTP | Route | Mô tả |
|---|---|---|
| POST | `/dm/ingest/json` | Ingest 1 record JSON |
| POST | `/dm/ingest/file` | Upload file `.json` hoặc `.csv` (max 50MB) — batch ingest |

#### SourcesController (`/dm/sources`)

| HTTP | Route | Mô tả |
|---|---|---|
| POST | `/dm/sources` | Đăng ký SourceProfile mới |
| GET | `/dm/sources?sourceSystem=...` | Liệt kê profile (optional filter) |

#### RecordsController (`/dm/records`)

| HTTP | Route | Mô tả |
|---|---|---|
| GET | `/dm/records/{id}` | Lấy 1 record theo ID |
| GET | `/dm/records?sourceSystem=&recordType=&field=&value=&from=&to=&limit=` | Search nhiều điều kiện. `field/value` filter trên `CanonicalPayload` (case-sensitive). `limit` mặc định 200, max 1000. |

#### ReportsController (`/dm/reports`)

| HTTP | Route | Mô tả |
|---|---|---|
| GET | `/dm/reports/{reportCode}?sourceSystem=&recordType=&from=&to=` | Báo cáo aggregate. Code có sẵn: `chi-phi-theo-khoa`, `benh-nhan-theo-khoa`, `tong-hop-nguon`. |

#### DashboardsController (`/dm/dashboards`)

| HTTP | Route | Mô tả |
|---|---|---|
| GET | `/dm/dashboards` | Danh sách dashboard code |
| GET | `/dm/dashboards/{code}?sourceSystem=&date=` | Render dashboard SDUI (KPI, Chart, Table sections) |

#### PagesController (`/dm/pages`)

| HTTP | Route | Mô tả |
|---|---|---|
| GET | `/dm/pages` | Danh sách page code |
| GET | `/dm/pages/{code}?sourceSystem=&date=` | Render SDUI page với KpiCard, ProgressList, AlertList, FlowPipeline, ChartPie components |

### 3.5 Luồng Ingest chi tiết — Từ HTTP request đến record lưu vào DB

#### 3.5.1 Test thật

```bash
curl -X POST "https://192.168.100.60:8443/dm/ingest/json" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceSystem": "his-fresher",
    "recordType": "benh-nhan",
    "payload": {
      "ma_bn":     "BN-FRESH-001",
      "ho_ten":    "Phạm Quỳnh Như",
      "ngay_sinh": "1992-08-14",
      "ten_khoa":  "Khoa Tim Mạch",
      "so_giuong": "TM-12",
      "chan_doan": "Rối loạn nhịp tim, theo dõi 24h",
      "bac_si":    "BS. Trần Văn Đạt"
    }
  }'
```

**Response (HTTP 202 Accepted):**

```json
{
  "success": true,
  "data": {
    "id": "e386531f-786d-4efa-86d8-e876dd200f14",
    "sourceSystem": "his-fresher",
    "recordType": "benh-nhan",
    "businessKey": "BN-FRESH-001",
    "status": "Pending"
  }
}
```

Tại sao **202 Accepted** không phải **201 Created**? Vì record được lưu nhưng **chưa xử lý xong** — Status = `Pending`. MatchingWorker sẽ xử lý ngầm sau đó.

#### 3.5.2 7 bước xử lý bên trong `IngestJsonHandler`

```
┌──────────────────────────────────────────────────────────────────────┐
│                                                                      │
│ Input từ Controller:                                                 │
│   IngestJsonCommand {                                                │
│     SourceSystem: "his-fresher",                                     │
│     RecordType:   "benh-nhan",                                       │
│     RawPayload:   '{"ma_bn":"BN-FRESH-001","ho_ten":"Phạm...",...}'  │
│     BusinessKeyOverride: null                                        │
│   }                                                                  │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│ BƯỚC 1: Tìm SourceProfile                                            │
│   profile = sources.GetBySystemAndTypeAsync("his-fresher",           │
│                                              "benh-nhan")            │
│   if (profile == null)                                               │
│     return Failure(NotFound("SourceProfile 'X/Y' not found"))        │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│ BƯỚC 2: Lấy mappings + áp dụng                                       │
│   mappings = profile.GetMappings()                                   │
│   // { "ma_bn":"MaBenhNhan", "ho_ten":"HoTen", ... }                 │
│                                                                      │
│   canonicalPayload = ApplyMappings(rawPayload, mappings)             │
│   // {                                                               │
│   //   "MaBenhNhan":    "BN-FRESH-001",                              │
│   //   "HoTen":         "Phạm Quỳnh Như",                            │
│   //   "NgaySinh":      "1992-08-14",                                │
│   //   "TenKhoa":       "Khoa Tim Mạch",                             │
│   //   "SoGiuong":      "TM-12",                                     │
│   //   "ChanDoan":      "Rối loạn nhịp tim, theo dõi 24h",           │
│   //   "BacSiPhuTrach": "BS. Trần Văn Đạt"                           │
│   // }                                                               │
│                                                                      │
│   // Field nào không có trong mappings → giữ nguyên tên gốc          │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│ BƯỚC 3: Trích BusinessKey                                            │
│   businessKey = request.BusinessKeyOverride                          │
│              ?? ExtractBusinessKey(canonicalPayload,                 │
│                                    profile.BusinessKeyField)        │
│   // Đọc canonicalPayload["MaBenhNhan"] = "BN-FRESH-001"             │
│   // businessKey = "BN-FRESH-001"                                    │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│ BƯỚC 4: Tính SHA-256 hash của RawPayload                             │
│   payloadHash = ComputeHash(rawPayload)                              │
│   // "a1b2c3d4..." (64 hex chars)                                    │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│ BƯỚC 5: Check duplicate                                              │
│   if (await records.ExistsHashAsync(payloadHash))                    │
│     return Failure(Conflict("Duplicate payload..."))                 │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│ BƯỚC 6: Tạo StagingRecord                                            │
│   record = StagingRecord.Receive(                                    │
│     sourceSystem: "his-fresher",                                     │
│     recordType:   "benh-nhan",                                       │
│     rawPayload:   '{"ma_bn":"BN-FRESH-001",...}',                    │
│     canonicalPayload: '{"MaBenhNhan":"BN-FRESH-001",...}',           │
│     businessKey:  "BN-FRESH-001",                                    │
│     payloadHash:  "a1b2c3d4...")                                     │
│   // Status = Pending (default)                                      │
│   // ReceivedAt = DateTime.UtcNow                                    │
│   // Id = Guid.NewGuid()                                             │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│ BƯỚC 7: Lưu DB                                                       │
│   await records.AddAsync(record, ct)                                 │
│   await uow.SaveChangesAsync(ct)                                     │
│                                                                      │
│ Return: IngestResultDto(record.Id, ..., status="Pending")            │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

#### 3.5.3 Vì sao dùng SHA-256?

**Câu hỏi:** Tại sao không dùng MD5 (nhanh hơn)?

**Trả lời:**
- MD5 có collision đã được chứng minh — 2 input khác nhau có thể cùng hash. Trong y tế, mất 1 record bệnh nhân là **không chấp nhận**.
- SHA-256: collision chưa từng tìm thấy, an toàn cho mục đích dedup.
- 64 ký tự hex = 256 bit. Xác suất 2 hash trùng ngẫu nhiên ≈ 1/2^128, gần như 0.

**Câu hỏi:** Hash của cái gì? Của `rawPayload` (JSON gốc) hay `canonicalPayload`?

**Trả lời:** Hash của `rawPayload`. Vì:
- Cùng dữ liệu nguồn → cùng hash → coi là trùng
- Nếu HIS đổi mapping (vd: thêm field mới) → hash khác → ingest mới (đúng — vì là phiên bản mới)

**Edge case cần biết:** Hash phụ thuộc vào **whitespace và order** của JSON. Hai request gửi cùng nội dung nhưng JSON formatter khác nhau (vd indent vs minified) → hash khác → coi là KHÔNG trùng.

Trong thực tế nếu HIS bệnh viện gửi lại đúng payload (cùng formatter) → hash trùng → 409 Conflict. Đó là behavior mong đợi.

#### 3.5.4 Vì sao dùng JSONB + GIN index?

`CanonicalPayload` lưu kiểu **JSONB** (binary JSON), không phải TEXT. Lý do:

- **Search trong JSON nhanh**: `WHERE CanonicalPayload @> '{"TenKhoa":"Tim Mạch"}'` chạy chỉ vài ms với GIN index, kể cả 100 triệu record.
- **Lưu compact**: Postgres compress jsonb, tiết kiệm 20-30% dung lượng.
- **Index linh hoạt**: GIN với `jsonb_path_ops` index TẤT CẢ key/value trong JSON.

Endpoint `GET /dm/records?field=TenKhoa&value=Tim+Mach` dùng chính GIN index này.

### 3.6 MatchingWorker chi tiết

`MatchingWorker` là **BackgroundService** (chạy ngầm) đăng ký trong DI ở `Program.cs`:

```csharp
services.AddHostedService<MatchingWorker>();
```

#### 3.6.1 Vòng đời

```
.NET app khởi động
       ↓
MatchingWorker.StartAsync() được gọi
       ↓
ExecuteAsync(stoppingToken) chạy đến khi app shutdown
       ↓
   while (!stoppingToken.IsCancellationRequested)
   {
     try { await ProcessBatchAsync(stoppingToken); }
     catch (Exception ex) when (!(ex is OperationCanceledException))
     { logger.LogError(ex, "..."); }   // Không throw, tiếp tục
     
     await Task.Delay(30s, stoppingToken);
   }
       ↓
.NET app shutdown → stoppingToken.Cancel() → vòng lặp dừng
```

#### 3.6.2 ProcessBatchAsync chi tiết

```
1. Tạo service scope (vì BackgroundService là Singleton, repo là Scoped)
   var scope = scopeFactory.CreateScope()
   var records = scope.ServiceProvider.GetRequiredService<IStagingRecordRepository>()
   var uow     = scope.ServiceProvider.GetRequiredService<IDataMatchingUnitOfWork>()

2. Lấy batch
   batch = await records.GetPendingBatchAsync(batchSize: 50, ct)
   // SQL: SELECT * FROM StagingRecords 
   //      WHERE Status = 'Pending' 
   //      ORDER BY ReceivedAt 
   //      LIMIT 50
   
   if (batch.Count == 0) return

3. Xử lý từng record
   foreach (var record in batch)
   {
     try
     {
       record.MarkProcessing();  // Status: Pending → Processing
       
       var matchedKey = string.IsNullOrWhiteSpace(record.BusinessKey)
         ? $"{record.SourceSystem}::{record.PayloadHash}"
         : $"{record.SourceSystem}::{record.BusinessKey}";
       // Ví dụ: "his-fresher::BN-FRESH-001"
       
       record.MarkMatched(matchedKey);  // Status: Processing → Matched, set ProcessedAt
     }
     catch (Exception ex)
     {
       logger.LogWarning(ex, "Failed to match record {Id}", record.Id);
       record.MarkFailed(ex.Message);  // Status: Failed, save FailureReason
     }
   }

4. Commit
   await uow.SaveChangesAsync(ct)

5. Log
   logger.LogInformation("MatchingWorker processed {Count} records.", batch.Count)
```

#### 3.6.3 Vòng đời record

```
                          MatchingWorker
                            chạy 30s/lần
   ┌─────────┐  Ingest  ┌────────┐    ┌─────────┐
   │         │ ────────►│        │───►│         │  Có thể query qua
   │   HIS   │          │Pending │    │ Matched │  GET /dm/records/{id}
   │         │          │        │    │         │  hoặc /dm/records?...
   └─────────┘          └────┬───┘    └─────────┘
                             │
                             │ MarkProcessing()
                             ▼
                        ┌─────────┐
                        │Processing│ (trong-flight)
                        └────┬────┘
                             │
                             │ MarkMatched() [success]
                             │ MarkFailed()  [exception]
                             ▼
                        ┌─────────┐
                        │ Failed  │ ← cần investigate, không tự retry
                        └─────────┘
```

**Lưu ý:** Status `Duplicate` có trong enum nhưng hiện tại Worker không set — Duplicate được handle ngay trong Ingest handler (bước 5 trên).

#### 3.6.4 Test thật: chờ record chuyển từ Pending → Matched

Sau khi ingest record `e386531f-786d-4efa-86d8-e876dd200f14`:

```bash
sleep 35   # đủ cho Worker chạy 1 lần
curl "https://192.168.100.60:8443/dm/records/e386531f-786d-4efa-86d8-e876dd200f14" \
  -H "Authorization: Bearer $TOKEN"
```

**Response (test thật):**

```json
{
  "success": true,
  "data": {
    "id": "e386531f-786d-4efa-86d8-e876dd200f14",
    "sourceSystem": "his-fresher",
    "recordType": "benh-nhan",
    "businessKey": "BN-FRESH-001",
    "status": "Matched",
    "canonicalPayload": "{\"HoTen\":\"Phạm Quỳnh Như\",\"TenKhoa\":\"Khoa Tim Mạch\",\"ChanDoan\":\"Rối loạn nhịp tim, theo dõi 24h\",\"NgaySinh\":\"1992-08-14\",\"SoGiuong\":\"TM-12\",\"MaBenhNhan\":\"BN-FRESH-001\",\"BacSiPhuTrach\":\"BS. Trần Văn Đạt\"}",
    "receivedAt": "2026-06-04T03:33:08.881064Z",
    "processedAt": "2026-06-04T03:33:15.902265Z"
  }
}
```

**Quan sát:**
- `ReceivedAt` = 03:33:08 (lúc ingest)
- `ProcessedAt` = 03:33:15 (7 giây sau, vì Worker tick rơi vào đúng lúc đó)
- `canonicalPayload` đã được rename: `ho_ten` → `HoTen`, `ngay_sinh` → `NgaySinh`...

#### 3.6.5 Vì sao xử lý batch chứ không từng record?

| Lý do | Giải thích |
|---|---|
| **Throughput cao hơn** | Mở connection DB 1 lần xử lý 50 record nhanh hơn mở 50 lần. |
| **Tránh quá tải** | Nếu HIS push 10,000 record/giờ, batch 50 = 200 batch, mỗi batch <1s — chấp nhận được. Xử lý 1 record/1 transaction sẽ thrash CPU/DB. |
| **Transaction isolation** | 1 batch = 1 transaction. Nếu DB lỗi giữa batch → rollback cả batch, retry lần sau. Đảm bảo nhất quán. |
| **Latency có thể chấp nhận** | Trong ngữ cảnh hospital, độ trễ 30s từ HIS gửi đến record sẵn sàng là OK. Form thường được mở sau hàng phút/giờ. |

### 3.7 Truy xuất dữ liệu

```bash
# Lấy 1 record
GET /dm/records/{id}

# Search theo điều kiện (tận dụng GIN index)
GET /dm/records?sourceSystem=his-fresher&recordType=benh-nhan
GET /dm/records?field=TenKhoa&value=Khoa+Tim+Mạch
GET /dm/records?from=2026-06-01T00:00:00&to=2026-06-30T23:59:59&limit=100
```

**Lưu ý:** `field/value` filter dùng JSON containment query trên `CanonicalPayload`. **Case-sensitive**: `value=Tim+Mạch` không khớp `Tim Mach`.

---

## 4. DynamicFormService

### 4.1 Vấn đề nó giải quyết

> Cho phép **admin tạo form qua API**, không cần dev code lại frontend mỗi lần thêm field mới.

### 4.2 Cấu trúc phân cấp — Sơ đồ ER đầy đủ

DynamicFormService có **8 bảng** trong PostgreSQL:

```
┌──────────────────────┐
│   FormModules        │  ← Nhóm form theo nghiệp vụ
├──────────────────────┤
│ Id, Code, Name       │
│ Status, ...          │
└────────┬─────────────┘
         │ 1:N
    ┌────┴────────────────────────┐
    ▼                             ▼
┌──────────────────────┐    ┌──────────────────────┐
│   FormTemplates      │    │   FormScreens        │
├──────────────────────┤    ├──────────────────────┤
│ Id, ModuleId, Key    │    │ Id, ModuleId, Code   │
│ Name, Description    │    │ Title, Description   │
│ Status, Version      │    │ Status, SortOrder    │
│ SettingsJson         │    │ DataSourcesJson      │ ← jsonb
└────────┬─────────────┘    └────────┬─────────────┘
         │ 1:N                       │ 1:N
         ▼                           ▼
┌──────────────────────┐    ┌──────────────────────┐
│   FormFields         │    │   FormScreenTabs     │
├──────────────────────┤    ├──────────────────────┤
│ Id, FormTemplateId   │    │ Id, ScreenId         │
│ Key, Label, Type     │    │ Label, Slug          │
│ Required, Width      │    │ SortOrder, IsDefault │
│ OptionsJson          │    └────────┬─────────────┘
│ ValidationRulesJson  │             │ 1:N
│ ConditionalLogicJson │             ▼
│ DataBindingJson  ←──┐│    ┌──────────────────────┐
│ IsReadOnly           ││   │   FormScreenWidgets  │
└──────────────────────┘│   ├──────────────────────┤
                        │   │ Id, TabId            │
                        │   │ WidgetKey, WidgetType│
                        │   │ GridX,Y,W,H          │
                        │   │ ConfigJson           │ ← jsonb
                        │   │ ReferenceId ─────────┼─→ FormTemplates.Id
                        │   └──────────────────────┘    (nếu WidgetType=FormSection)
                        │
                        │   ┌──────────────────────┐
                        │   │   FormSubmissions    │
                        │   ├──────────────────────┤
                        └───┤ Id, FormTemplateId   │
                            │ ModuleCode, FormKey  │
                            │ FormVersion          │ ← copy snapshot
                            │ SubmittedBy          │
                            │ Status, SubmittedAt  │
                            │ AnswersJson          │
                            └──────────────────────┘

   ┌──────────────────────┐  (độc lập, không FK)
   │   WidgetCatalogs     │  ← Metadata các widget có sẵn (chart, table...)
   ├──────────────────────┤
   │ Id, ChartType        │
   │ Category, Label      │
   │ Icon, SortOrder      │
   │ RequiredColumnsJson  │
   │ OptionalColumnsJson  │
   │ CompatibleWithJson   │
   └──────────────────────┘
```

**Hãy hình dung như tòa nhà:**

```
FormModule          (Tòa nhà)            — vd: "fresher-demo"
  ├─ FormScreen     (Tầng)               — vd: "patient-review"
  │    └─ Tab       (Phòng trong tầng)   — vd: "Thông tin chính"
  │         └─ Widget (Đồ đạc trong phòng) — vd: FormSection, KpiCard, Chart
  └─ FormTemplate   (Bàn làm việc dùng chung)
        └─ FormField (Ngăn kéo trên bàn) — vd: hoten, ngaysinh, ket_luan
```

Widget **FormSection** "đặt" 1 FormTemplate vào trong Tab — nghĩa là cùng 1 form có thể nhúng vào nhiều screen khác nhau.

### 4.3 Domain Entities — Chi tiết từng entity

#### 4.3.1 FormModule

```csharp
public sealed class FormModule : AggregateRoot<Guid>
{
    public string         Code         { get; private set; }   // "fresher-demo"
    public string         Name         { get; private set; }   // "Fresher Demo Module"
    public string?        Description  { get; private set; }
    public ModuleStatus   Status       { get; private set; }   // Active | Inactive
    public DateTime       CreatedAtUtc { get; private set; }
    public DateTime?      UpdatedAtUtc { get; private set; }

    public static FormModule Create(string code, string name, string? description);
    public void Update(string name, string? description);
    public void Deactivate();
    public void Activate();
}
```

**Domain Event:** `FormModuleCreatedDomainEvent` (raise khi `Create`).

#### 4.3.2 FormTemplate

```csharp
public sealed class FormTemplate : AggregateRoot<Guid>
{
    public Guid           ModuleId      { get; private set; }
    public string         ModuleCode    { get; private set; }   // denormalized
    public string         Key           { get; private set; }   // "patient-review-form"
    public string         Name          { get; private set; }
    public string?        Description   { get; private set; }
    public FormStatus     Status        { get; private set; }   // Draft|Published|Archived
    public int            Version       { get; private set; }   // ↑ mỗi lần Publish
    public string         SettingsJson  { get; private set; }   // jsonb FormSettings

    public IReadOnlyCollection<FormField> Fields { get; }

    public static FormTemplate Create(
        Guid moduleId, string moduleCode, string key, string name,
        string? description, FormSettings settings);

    public FormField AddField(
        string key, string label, FieldType fieldType, int order,
        bool required, FieldWidth width,
        string? placeholder, string? helpText,
        List<FieldOption>? options,
        List<ValidationRule>? rules,
        ConditionalLogic? conditional,
        DataBinding? dataBinding,
        bool isReadOnly);

    public void Publish();   // Status: Draft → Published, Version++
    public void Archive();   // Status: → Archived
    public void Update(string name, string? description, FormSettings settings);
}
```

**Guards:**
- `AddField()` throw nếu Status = Published (phải Archive cũ → tạo mới)
- `Update()` throw nếu Status = Published

**Domain Event:** `FormPublishedDomainEvent` (raise khi `Publish`).

#### 4.3.3 FormField

```csharp
public sealed class FormField : BaseEntity<Guid>
{
    public Guid       FormTemplateId       { get; private set; }
    public string     Key                  { get; private set; }   // "hoten"
    public string     Label                { get; private set; }   // "Họ tên"
    public FieldType  FieldType            { get; private set; }   // Text|Date|Select...
    public int        Order                { get; private set; }
    public bool       Required             { get; private set; }
    public FieldWidth Width                { get; private set; }   // Full|Half|Third
    public string?    Placeholder          { get; private set; }
    public string?    HelpText             { get; private set; }
    public string?    OptionsJson          { get; private set; }   // jsonb List<FieldOption>
    public string?    ValidationRulesJson  { get; private set; }   // jsonb List<ValidationRule>
    public string?    ConditionalLogicJson { get; private set; }   // jsonb ConditionalLogic
    public string?    DataBindingJson      { get; private set; }   // jsonb DataBinding (expression)
    public bool       IsReadOnly           { get; private set; }   // default false
}
```

#### 4.3.4 FormScreen

```csharp
public sealed class FormScreen : AggregateRoot<Guid>
{
    public Guid       ModuleId         { get; private set; }
    public string     ModuleCode       { get; private set; }
    public string     Code             { get; private set; }       // "patient-review"
    public string     Title            { get; private set; }
    public string?    Description      { get; private set; }
    public FormStatus Status           { get; private set; }       // Draft|Published|Archived
    public int        SortOrder        { get; private set; }
    public string?    DataSourcesJson  { get; private set; }       // jsonb List<DataSource>

    public IReadOnlyCollection<FormScreenTab> Tabs { get; }

    public static FormScreen Create(
        Guid moduleId, string moduleCode, string code, string title,
        string? description, int sortOrder);

    public void Update(string title, string? description, int sortOrder);
    public void Publish();
    public void Archive();

    public FormScreenTab AddTab(string label, string slug, int sortOrder, bool isDefault);
    public void RemoveTab(Guid tabId);
    public void SetDataSources(List<DataSource> sources);   // full replacement
}
```

**Guards:**
- `Update()`, `AddTab()`, `SetDataSources()` throw nếu Status = Archived

**Domain Event:** `FormScreenPublishedDomainEvent`.

#### 4.3.5 FormScreenTab

```csharp
public sealed class FormScreenTab : BaseEntity<Guid>
{
    public Guid    ScreenId  { get; private set; }
    public string  Label     { get; private set; }   // "Thông tin"
    public string  Slug      { get; private set; }   // "main", "xet-nghiem"
    public int     SortOrder { get; private set; }
    public bool    IsDefault { get; private set; }

    public IReadOnlyCollection<FormScreenWidget> Widgets { get; }

    public void Update(string label, int sortOrder, bool isDefault);
    public void ReplaceWidgets(IEnumerable<FormScreenWidget> widgets);   // full replacement
}
```

`ReplaceWidgets` thay TOÀN BỘ widget — dùng khi admin "Lưu" sau khi drag-drop.

#### 4.3.6 FormScreenWidget

```csharp
public sealed class FormScreenWidget : BaseEntity<Guid>
{
    public Guid    TabId       { get; private set; }
    public string  WidgetKey   { get; private set; }   // "form-main", "chart-1"
    public string  WidgetType  { get; private set; }   // "FormSection", "KpiCard", "Table"
    public int     GridX       { get; private set; }   // React Grid Layout
    public int     GridY       { get; private set; }
    public int     GridW       { get; private set; }
    public int     GridH       { get; private set; }
    public string  ConfigJson  { get; private set; }   // jsonb, config tùy widgetType
    public Guid?   ReferenceId { get; private set; }   // nếu FormSection → FormTemplate.Id

    public static FormScreenWidget Create(
        Guid tabId, string widgetKey, string widgetType,
        int gridX, int gridY, int gridW, int gridH,
        string configJson, Guid? referenceId);
}
```

#### 4.3.7 FormSubmission

```csharp
public sealed class FormSubmission : AggregateRoot<Guid>
{
    public Guid            FormTemplateId { get; private set; }
    public string          ModuleCode     { get; private set; }
    public string          FormKey        { get; private set; }
    public int             FormVersion    { get; private set; }   // SNAPSHOT lúc submit
    public Guid?           SubmittedBy    { get; private set; }   // userId, nullable
    public SubmissionStatus Status        { get; private set; }   // Submitted|Reviewed
    public string          AnswersJson    { get; private set; }   // jsonb List<FieldAnswer>
    public DateTime        SubmittedAt    { get; private set; }

    public static FormSubmission Create(
        Guid formTemplateId, string moduleCode, string formKey,
        int formVersion, Guid? submittedBy, List<FieldAnswer> answers);

    public void MarkReviewed();
}
```

**Quan trọng:** `FormVersion` ghi nhớ version form **LÚC submit**. Nếu sau này form được update lên v3, submission cũ vẫn gắn v2 — audit chính xác.

#### 4.3.8 WidgetCatalog

Metadata mô tả widget có sẵn (cho UI designer hiển thị tool palette):

```csharp
public sealed class WidgetCatalog
{
    public string ChartType  { get; private set; }   // "kpi-card", "pie-chart"
    public string Category   { get; private set; }   // "chart", "display"
    public string Label      { get; private set; }
    public string Description{ get; private set; }
    public string Icon       { get; private set; }
    public string RowSchema  { get; private set; }   // jsonb
    public string RequiredColumnsJson { get; private set; }
    public string OptionalColumnsJson { get; private set; }
    public string CompatibleWithJson  { get; private set; }
    public int    SortOrder  { get; private set; }
}
```

### 4.4 Value Objects (7)

Value Object = immutable, so sánh bằng giá trị. Trong code là `sealed record`.

```csharp
// 1. Expression binding cho field
public sealed record DataBinding(
    string  Expression,        // "{{sources.record.HoTen}}"
    string? DisplayFormat);    // "date:DD/MM/YYYY" | "currency:VND" | null

// 2. Khai báo nguồn dữ liệu cho screen
public sealed record DataSource(
    string       Namespace,        // "record"  (key trong sources)
    string       ServiceId,        // "datamatch"
    string       ResourcePath,     // "/dm/records/{recordId}"
    List<string> RequiredParams);  // ["recordId"]

// 3. Option cho field type Select/Radio/Checkbox
public sealed record FieldOption(string Label, string Value);

// 4. Rule validate
public sealed record ValidationRule(
    string Type,           // "required"|"minLength"|"maxLength"|"pattern"|"min"|"max"
    string Value,          // "5"  hoặc  "^[A-Z]{3}$"
    string ErrorMessage);

// 5. Logic hiển thị có điều kiện
public sealed record ConditionalLogic(
    string SourceFieldKey,  // field nào trigger
    string Operator,        // "Equals" | "NotEquals" | "Contains"
    string Value,           // "Khác"
    string Action);         // "Show" | "Hide"

// 6. Cấu hình chung của form
public sealed record FormSettings(
    string SubmitButtonLabel       = "Gửi",
    string SuccessMessage          = "Đã gửi form thành công",
    bool   AllowMultipleSubmissions = true);

// 7. Câu trả lời của user
public sealed record FieldAnswer(string FieldKey, string? Value);
```

### 4.5 Enums (7)

```csharp
public enum FormStatus       { Draft = 0, Published = 1, Archived = 2 }
public enum SubmissionStatus { Submitted = 0, Reviewed = 1 }
public enum ModuleStatus     { Active = 0, Inactive = 1 }
public enum FieldType
{
    Text=0, Textarea=1, Number=2, Date=3, DateTime=4,
    Select=5, MultiSelect=6, Radio=7, Checkbox=8,
    File=9, Signature=10, Section=11
}
public enum FieldWidth  { Full = 0, Half = 1, Third = 2 }
public enum WidgetType
{
    FormSection=0, TextBlock=1, Divider=2,
    ImageBlock=3, ConditionalSection=4
}
public enum FormPageStatus { Draft=0, Published=1, Archived=2 }   // unused, dùng FormStatus
```

### 4.6 Status Lifecycle — Draft → Published → Archived

Cả `FormTemplate` và `FormScreen` đều dùng enum `FormStatus`:

```
                    ┌──────────────┐
                    │ FormTemplate │
                    │  hoặc Screen │
                    └──────┬───────┘
                           │
                       Create()
                           │
                           ▼
                    ┌──────────────┐
       ┌────────────│    Draft     │◄──────────┐
       │            └──────┬───────┘           │
       │                   │                   │
       │  AddField/        │  Publish()        │
       │  AddTab/          │                   │
       │  Update           │                   │
       │  ✓ ALLOWED        ▼                   │
       │            ┌──────────────┐           │
       │   ❌ ERROR │  Published   │           │
       │   nếu cố   └──────┬───────┘           │
       │   Update            │                 │
       │                     │ Archive()       │
       │                     ▼                 │
       │            ┌──────────────┐           │
       └───────────►│   Archived   │           │
                    └──────────────┘           │
                       ❌ ALL OPERATIONS         │
                       BLOCKED                 │
                                               │
                       (Tạo mới Draft khác)────┘
```

#### Quy tắc

| Status | Có thể đọc? | Có thể sửa? | Form có hiển thị frontend? |
|---|---|---|---|
| `Draft` | ✓ (chỉ admin endpoint) | ✓ | ❌ Không |
| `Published` | ✓ (cả public endpoint) | ❌ (must Archive trước) | ✓ Có |
| `Archived` | ✓ (chỉ history) | ❌ | ❌ Không |

#### Lỗi thường gặp khi vi phạm

```csharp
// Code:
template.AddField("ten", ...);   // template.Status = Published
// Throws:
InvalidOperationException("Cannot add field to a published form. Archive first.")

// Code:
screen.AddTab("New Tab", ...);   // screen.Status = Archived
// Throws:
InvalidOperationException("Cannot modify an archived screen.")
```

### 4.7 Versioning

#### Cơ chế

`FormTemplate.Version`:
- Khởi tạo = `1` khi `Create()`
- Tăng `Version++` mỗi lần gọi `Publish()`

```
Lúc create:        Version = 1, Status = Draft
Lần Publish 1:    Version = 2, Status = Published  ← test thật trả về v2
                  (Archive)
                  (Tạo lại từ Draft, add fields mới)
Lần Publish 2:    Version = 3, Status = Published
```

**Vì sao tăng từ 1 → 2 khi publish lần đầu?** Vì `Publish()` thực hiện `Version++` luôn (không check version hiện tại). Đây là cách implement đơn giản — version `1` coi như "draft initial", version `≥2` là "published từng lần".

#### FormSubmission gắn version

```csharp
var submission = FormSubmission.Create(
    template.Id,
    template.ModuleCode,
    template.Key,
    template.Version,    // ← SNAPSHOT
    submittedBy,
    answers);
```

**Ích lợi:**
- Audit: biết user submit với version nào → mapping được câu trả lời đúng nghĩa
- Backward compatibility: form thay đổi (đổi label, thêm field) nhưng submission cũ vẫn diễn giải được
- Migration: có thể viết script convert answers v2 → v3 nếu cần

#### Không tự động migrate

Nếu form đổi v2 → v3, submission cũ **không tự update**. Phải viết migration thủ công khi cần (hiếm gặp — thường không cần).

### 4.8 ConditionalLogic & ValidationRules

#### ConditionalLogic — hiển thị field có điều kiện

```json
{
  "key": "ly_do_khac",
  "label": "Lý do khác (vui lòng ghi rõ)",
  "type": "Textarea",
  "conditionalLogic": {
    "sourceFieldKey": "ly_do",
    "operator": "Equals",
    "value": "Khác",
    "action": "Show"
  }
}
```

**Đọc như sau:** "Hiển thị field `ly_do_khac` chỉ khi field `ly_do` = `Khác`".

`operator` hỗ trợ: `Equals`, `NotEquals`, `Contains`.
`action`: `Show` (mặc định ẩn, hiện khi đúng điều kiện) | `Hide` (mặc định hiện, ẩn khi đúng).

Backend chỉ **lưu** rule này. Frontend chịu trách nhiệm evaluate và show/hide.

#### ValidationRules — kiểm tra dữ liệu

```json
{
  "key": "so_dien_thoai",
  "label": "Số điện thoại",
  "type": "Text",
  "validationRules": [
    { "type": "required",  "value": "",                   "errorMessage": "Vui lòng nhập số điện thoại" },
    { "type": "pattern",   "value": "^0[0-9]{9}$",       "errorMessage": "Số điện thoại không hợp lệ" },
    { "type": "minLength", "value": "10",                "errorMessage": "Tối thiểu 10 ký tự" }
  ]
}
```

**Các `type` hỗ trợ:**

| Type | Value | Áp dụng cho |
|---|---|---|
| `required` | `""` (không dùng) | Mọi field |
| `minLength` | số nguyên | Text, Textarea |
| `maxLength` | số nguyên | Text, Textarea |
| `pattern` | regex | Text, Textarea |
| `min` | số | Number, Date |
| `max` | số | Number, Date |

Validate xảy ra **2 chỗ:**
- **Frontend** (client-side): hiển thị error realtime
- **Backend khi submit**: chặn payload bad. Hiện tại implementation chưa hoàn thiện — frontend validate trước là chính.

### 4.9 Tất cả ~40 API endpoints

DynamicFormService có **5 controllers**, route prefix `/forms`:

#### 4.9.1 FormsController (`/forms`) — Public

| HTTP | Route | Command/Query | Mô tả |
|---|---|---|---|
| GET | `/forms/health` | — | Health check |
| GET | `/forms/modules` | `GetModulesQuery` | Danh sách module + thông tin tóm tắt (FormCount, ScreenCount, danh sách Pages) |
| GET | `/forms/{moduleCode}/pages` | `GetPublishedScreensByModuleQuery` | Danh sách screen đã Published của module |
| GET | `/forms/{moduleCode}` | `GetFormsByModuleQuery` | Danh sách form trong module |
| GET | `/forms/{moduleCode}/{formKey}/schema` | `GetFormSchemaQuery` | **BDUI** — Form schema để render form động (fields + settings) |
| POST | `/forms/{moduleCode}/{formKey}/submit` | `SubmitFormCommand` | Submit câu trả lời |

#### 4.9.2 ScreensController (`/forms/screens`) — Public SDUI

| HTTP | Route | Command/Query | Mô tả |
|---|---|---|---|
| GET | `/forms/screens/{moduleCode}` | `GetScreensQuery` | Danh sách screen Published của module |
| GET | `/forms/screens/{moduleCode}/{screenCode}/layout` | `GetScreenLayoutQuery` | **SDUI** — Layout đầy đủ: DataSources + Tabs + Widgets + (FormSchema hydrate nếu FormSection) |

#### 4.9.3 AdminFormsController (`/forms/admin`) — Admin

| HTTP | Route | Command/Query | Mô tả |
|---|---|---|---|
| POST | `/forms/admin/modules` | `CreateModuleCommand` | Tạo module |
| POST | `/forms/admin/modules/{moduleCode}/forms` | `CreateFormCommand` | Tạo form trong module |
| POST | `/forms/admin/forms/{formId}/fields` | `AddFieldCommand` | Thêm field vào form (chỉ Draft) |
| POST | `/forms/admin/forms/{formId}/publish` | `PublishFormCommand` | Publish form (Draft → Published, Version++) |
| POST | `/forms/admin/forms/{formId}/archive` | `ArchiveFormCommand` | Archive form |
| GET | `/forms/admin/forms/{formId}/submissions?page=&pageSize=` | `GetSubmissionsQuery` | Danh sách submission (paginated) |

#### 4.9.4 AdminPagesController (`/forms/admin/{moduleCode}/pages`) — Quản lý page (alias của screen)

| HTTP | Route | Command/Query | Mô tả |
|---|---|---|---|
| GET | `/forms/admin/{moduleCode}/pages` | `GetScreensQuery` | Danh sách page |
| POST | `/forms/admin/{moduleCode}/pages` | `CreateScreenCommand` | Tạo page |
| PUT | `/forms/admin/{moduleCode}/pages/{pageCode}` | `UpdateScreenCommand` | Update page |
| DELETE | `/forms/admin/{moduleCode}/pages/{pageCode}` | `DeleteScreenCommand` | Xóa page |
| POST | `/forms/admin/{moduleCode}/pages/{pageCode}/publish` | `PublishScreenCommand` | Publish page |
| POST | `/forms/admin/{moduleCode}/pages/{pageCode}/tabs` | `CreateTabCommand` | Tạo tab |
| PUT | `/forms/admin/{moduleCode}/pages/{pageCode}/tabs/{tabId}` | `UpdateTabCommand` | Update tab |
| DELETE | `/forms/admin/{moduleCode}/pages/{pageCode}/tabs/{tabId}` | `DeleteTabCommand` | Xóa tab |
| PUT | `/forms/admin/{moduleCode}/pages/{pageCode}/tabs/{tabId}/widgets` | `SaveTabWidgetsCommand` | Lưu drag-drop widget layout (full replacement) |

#### 4.9.5 AdminScreensController (`/forms/admin/screens`)

| HTTP | Route | Command/Query | Mô tả |
|---|---|---|---|
| GET | `/forms/admin/widget-catalog?category=` | `GetWidgetCatalogQuery` | Widget catalog cho UI designer |
| GET | `/forms/admin/screens/{moduleCode}` | `GetScreensQuery` | Danh sách screen |
| POST | `/forms/admin/screens` | `CreateScreenCommand` | Tạo screen |
| PUT | `/forms/admin/screens/{moduleCode}/{screenCode}` | `UpdateScreenCommand` | Update screen |
| DELETE | `/forms/admin/screens/{moduleCode}/{screenCode}` | `DeleteScreenCommand` | Xóa screen |
| POST | `/forms/admin/screens/{moduleCode}/{screenCode}/publish` | `PublishScreenCommand` | Publish screen |
| POST | `/forms/admin/generate-from-source` | `GenerateFromSourceCommand` | **AUTO** — tạo Module + Screen + DataSources + Form + Fields + Tab + Widget trong 1 command |
| PUT | `/forms/admin/screens/{moduleCode}/{screenCode}/data-sources` | `SetScreenDataSourcesCommand` | Khai báo DataSources cho screen (full replacement) |
| POST | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs` | `CreateTabCommand` | Tạo tab |
| PUT | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}` | `UpdateTabCommand` | Update tab |
| DELETE | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}` | `DeleteTabCommand` | Xóa tab |
| PUT | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}/widgets` | `SaveTabWidgetsCommand` | Lưu widget layout |

### 4.10 Generate-from-source — Chi tiết handler

Endpoint `POST /forms/admin/generate-from-source` là **bí mật** của auto-generation: tạo cả Module + Screen + DataSources + Form + Fields + Tab + Widget chỉ trong 1 lệnh.

#### 4.10.1 Test thật

```bash
curl -X POST "https://192.168.100.60:8443/forms/admin/generate-from-source" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "moduleCode":  "fresher-demo",
    "moduleName":  "Fresher Demo Module",
    "screenCode":  "patient-review",
    "screenTitle": "Xét duyệt hồ sơ bệnh nhân (Demo)",
    "formKey":     "patient-review-form",
    "formTitle":   "Phiếu xét duyệt bệnh nhân",
    "dataSource": {
      "namespace":      "record",
      "serviceId":      "datamatch",
      "resourcePath":   "/dm/records/{recordId}",
      "requiredParams": ["recordId"]
    },
    "fields": [
      { "canonicalKey": "HoTen",          "label": "Họ tên",           "fieldType": "Text" },
      { "canonicalKey": "NgaySinh",       "label": "Ngày sinh",        "fieldType": "Date",     "displayFormat": "date:DD/MM/YYYY" },
      { "canonicalKey": "TenKhoa",        "label": "Khoa",             "fieldType": "Text" },
      { "canonicalKey": "SoGiuong",       "label": "Số giường",        "fieldType": "Text" },
      { "canonicalKey": "ChanDoan",       "label": "Chẩn đoán",        "fieldType": "Textarea" },
      { "canonicalKey": "BacSiPhuTrach",  "label": "BS phụ trách",     "fieldType": "Text" },
      { "canonicalKey": null, "fieldKey": "ket_luan", "label": "Kết luận xét duyệt", "fieldType": "Select", "isReadOnly": false, "required": true, "options": ["Đạt tiêu chuẩn","Cần bổ sung","Không đạt"] },
      { "canonicalKey": null, "fieldKey": "ghi_chu",  "label": "Ghi chú",            "fieldType": "Textarea","isReadOnly": false }
    ]
  }'
```

**Response (HTTP 200 OK):**

```json
{
  "success": true,
  "data": {
    "moduleCode": "fresher-demo",
    "screenCode": "patient-review",
    "formKey": "patient-review-form",
    "formTemplateId": "7f87c94c-7772-4816-a709-d0fe68f1143d",
    "fieldsGenerated": 8
  }
}
```

#### 4.10.2 Quy ước input

| Field input | Bắt buộc | Quy ước |
|---|---|---|
| `canonicalKey` | optional | Nếu có → field **bound**, mặc định `isReadOnly=true`, tự tạo expression `{{sources.<ns>.<canonicalKey>}}` |
| `fieldKey` | bắt buộc nếu `canonicalKey=null` | Field **free**, user nhập, mặc định `isReadOnly=false`. Kiểu lowercase + dấu gạch dưới |
| `isReadOnly` | optional (`bool?`) | Nếu null → tự suy: `true` nếu bound, `false` nếu free. Có thể override |
| `fieldType` | optional, default `"Text"` | Một trong 12 giá trị enum FieldType |
| `displayFormat` | optional | Hint cho FE format: `date:DD/MM/YYYY`, `currency:VND`... |
| `options` | optional, dùng cho `Select`/`Radio` | `List<string>` — sẽ convert thành `[{Label,Value}]` cùng giá trị |
| `required` | optional, default false | Bắt buộc nhập |

#### 4.10.3 8 bước handler

```
┌──────────────────────────────────────────────────────────────────────┐
│ Input: GenerateFromSourceCommand                                     │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│ ┌────────────────────────────────────────────────────────────────┐   │
│ │ BƯỚC 1: Get hoặc auto-create Module                            │   │
│ │   module = await modules.GetByCodeAsync("fresher-demo")        │   │
│ │   if (module == null) {                                        │   │
│ │     module = FormModule.Create("fresher-demo",                 │   │
│ │                                "Fresher Demo Module", null)    │   │
│ │     await modules.AddAsync(module)                             │   │
│ │   }                                                            │   │
│ └────────────────────────────────────────────────────────────────┘   │
│                                                                      │
│ ┌────────────────────────────────────────────────────────────────┐   │
│ │ BƯỚC 2: Guard — Screen & Form không được tồn tại sẵn           │   │
│ │   if (screens.ExistsByCodeAsync("fresher-demo","patient-review"│   │
│ │      ))                                                        │   │
│ │     return Failure(Conflict("Screen 'X' đã tồn tại"))          │   │
│ │   if (templates.ExistsByKeyInModuleAsync(module.Id, formKey))  │   │
│ │     return Failure(Conflict("Form 'Y' đã tồn tại"))            │   │
│ └────────────────────────────────────────────────────────────────┘   │
│                                                                      │
│ ┌────────────────────────────────────────────────────────────────┐   │
│ │ BƯỚC 3: Tạo FormTemplate + tự add fields                       │   │
│ │   form = FormTemplate.Create(module.Id, "fresher-demo",        │   │
│ │            "patient-review-form", "Phiếu xét duyệt", null,     │   │
│ │            new FormSettings("Gửi","Đã gửi thành công",true))   │   │
│ │                                                                │   │
│ │   foreach (fi in request.Fields) {                             │   │
│ │     isBound  = fi.CanonicalKey != null                         │   │
│ │     fieldKey = isBound ? fi.CanonicalKey.ToLower()             │   │
│ │                        : fi.FieldKey                           │   │
│ │     binding  = isBound                                         │   │
│ │       ? new DataBinding(                                       │   │
│ │           $"{{{{sources.{ns}.{fi.CanonicalKey}}}}}",           │   │
│ │           fi.DisplayFormat)                                    │   │
│ │       : null                                                   │   │
│ │     isReadOnly = fi.IsReadOnly ?? isBound                      │   │
│ │     form.AddField(fieldKey, fi.Label, fieldType, order++, ...,  │   │
│ │                   binding, isReadOnly)                         │   │
│ │   }                                                            │   │
│ └────────────────────────────────────────────────────────────────┘   │
│                                                                      │
│ ┌────────────────────────────────────────────────────────────────┐   │
│ │ BƯỚC 4: Publish form (Draft → Published, Version 1 → 2)        │   │
│ │   form.Publish()                                               │   │
│ │   await templates.AddAsync(form)                               │   │
│ └────────────────────────────────────────────────────────────────┘   │
│                                                                      │
│ ┌────────────────────────────────────────────────────────────────┐   │
│ │ BƯỚC 5: Tạo Screen + khai báo DataSources                      │   │
│ │   screen = FormScreen.Create(module.Id, moduleCode,            │   │
│ │             screenCode, screenTitle, null, 0)                  │   │
│ │   screen.SetDataSources([                                      │   │
│ │     new DataSource("record","datamatch",                       │   │
│ │                    "/dm/records/{recordId}", ["recordId"])     │   │
│ │   ])                                                           │   │
│ └────────────────────────────────────────────────────────────────┘   │
│                                                                      │
│ ┌────────────────────────────────────────────────────────────────┐   │
│ │ BƯỚC 6: Tạo tab mặc định + widget FormSection                  │   │
│ │   tab = screen.AddTab("Thông tin", "main", 0, isDefault: true) │   │
│ │   widget = FormScreenWidget.Create(tab.Id, "form-main",        │   │
│ │             "FormSection", 0, 0, 24, 12, "{}", form.Id)        │   │
│ │   tab.ReplaceWidgets([widget])                                 │   │
│ └────────────────────────────────────────────────────────────────┘   │
│                                                                      │
│ ┌────────────────────────────────────────────────────────────────┐   │
│ │ BƯỚC 7: Publish screen                                         │   │
│ │   screen.Publish()                                             │   │
│ │   screens.Add(screen)                                          │   │
│ └────────────────────────────────────────────────────────────────┘   │
│                                                                      │
│ ┌────────────────────────────────────────────────────────────────┐   │
│ │ BƯỚC 8: Commit tất cả trong 1 transaction                      │   │
│ │   await uow.SaveChangesAsync(ct)                               │   │
│ │   // Nếu bất kỳ bước nào lỗi → rollback toàn bộ                │   │
│ └────────────────────────────────────────────────────────────────┘   │
│                                                                      │
│ Return: GenerateFromSourceResultDto                                  │
└──────────────────────────────────────────────────────────────────────┘
```

#### 4.10.4 Domain events raise

```
1. FormModuleCreatedDomainEvent       (nếu module mới)
2. FormPublishedDomainEvent           (khi form.Publish)
3. FormScreenPublishedDomainEvent     (khi screen.Publish)
```

Domain events được dispatch sau khi `SaveChangesAsync` thành công (do EF Core SaveChanges interceptor handle).

### 4.11 Layout query chi tiết

Endpoint `GET /forms/screens/{moduleCode}/{screenCode}/layout` — **SDUI** quan trọng nhất.

#### 4.11.1 Test thật

```bash
curl "https://192.168.100.60:8443/forms/screens/fresher-demo/patient-review/layout" \
  -H "Authorization: Bearer $TOKEN"
```

**Response (rút gọn):**

```json
{
  "success": true,
  "data": {
    "id": "...",
    "moduleCode": "fresher-demo",
    "code": "patient-review",
    "title": "Xét duyệt hồ sơ bệnh nhân (Demo)",
    "description": null,
    "dataSources": [
      {
        "namespace": "record",
        "serviceId": "datamatch",
        "resourcePath": "/dm/records/{recordId}",
        "requiredParams": ["recordId"]
      }
    ],
    "tabs": [
      {
        "id": "...",
        "label": "Thông tin",
        "slug": "main",
        "sortOrder": 0,
        "isDefault": true,
        "widgets": [
          {
            "widgetKey": "form-main",
            "widgetType": "FormSection",
            "gridX": 0, "gridY": 0, "gridW": 24, "gridH": 12,
            "config": {},
            "referenceId": "7f87c94c-7772-4816-a709-d0fe68f1143d",
            "formSchema": {
              "id": "7f87c94c-...",
              "moduleCode": "fresher-demo",
              "formKey": "patient-review-form",
              "name": "Phiếu xét duyệt bệnh nhân",
              "version": 2,
              "fields": [
                {
                  "id": "...",
                  "key": "hoten",
                  "label": "Họ tên",
                  "type": "Text",
                  "order": 0,
                  "required": false,
                  "width": "Full",
                  "isReadOnly": true,
                  "dataBinding": {
                    "expression": "{{sources.record.HoTen}}",
                    "displayFormat": null
                  }
                },
                {
                  "key": "ngaysinh",
                  "label": "Ngày sinh",
                  "type": "Date",
                  "isReadOnly": true,
                  "dataBinding": {
                    "expression": "{{sources.record.NgaySinh}}",
                    "displayFormat": "date:DD/MM/YYYY"
                  }
                },
                ...
                {
                  "key": "ket_luan",
                  "label": "Kết luận xét duyệt",
                  "type": "Select",
                  "required": true,
                  "isReadOnly": false,
                  "dataBinding": null,
                  "options": [
                    { "label": "Đạt tiêu chuẩn", "value": "Đạt tiêu chuẩn" },
                    { "label": "Cần bổ sung",    "value": "Cần bổ sung" },
                    { "label": "Không đạt",      "value": "Không đạt" }
                  ]
                }
              ],
              "settings": {
                "submitButtonLabel": "Gửi",
                "successMessage": "Đã gửi form thành công",
                "allowMultipleSubmissions": true
              }
            }
          }
        ]
      }
    ],
    "generatedAt": "2026-06-04T03:34:01Z"
  }
}
```

#### 4.11.2 Handler logic

```
1. Fetch screen với tabs + widgets
   screen = await screens.GetWithTabsAndWidgetsAsync(moduleCode, screenCode)
   if (screen == null) return NotFound

2. Deserialize DataSourcesJson → List<DataSourceDto>

3. Map tabs (ORDER BY SortOrder)
   foreach tab:
     foreach widget (ORDER BY ConfigJson hoặc tự nhiên):
       parse ConfigJson → object
       if (widgetType = "FormSection" AND referenceId != null):
         template = await templates.GetByIdAsync(referenceId, includeFields: true)
         formSchema = HydrateFormSchema(template)   // map đầy đủ fields
       else:
         formSchema = null

4. Return ScreenLayoutDto
```

#### 4.11.3 HydrateFormSchema

Đây là logic biến `FormTemplate` (entity) thành `FormSchemaDto` (DTO):

```csharp
FormSchemaDto HydrateFormSchema(FormTemplate t)
{
    var settings = JsonSerializer.Deserialize<FormSettings>(t.SettingsJson);
    var fields = t.Fields
        .OrderBy(f => f.Order)
        .Select(f => new FormFieldDto(
            f.Id, f.Key, f.Label, f.FieldType.ToString(),
            f.Order, f.Required, f.Width.ToString(),
            f.Placeholder, f.HelpText,
            DeserializeOrNull<List<FieldOptionDto>>(f.OptionsJson),
            DeserializeOrNull<List<ValidationRuleDto>>(f.ValidationRulesJson),
            DeserializeOrNull<ConditionalLogicDto>(f.ConditionalLogicJson),
            DeserializeOrNull<DataBindingDto>(f.DataBindingJson),
            f.IsReadOnly))
        .ToList();
    return new FormSchemaDto(t.Id, t.ModuleCode, t.Key, t.Name, t.Description,
                             t.Version, fields, MapSettings(settings));
}
```

### 4.12 Submit flow chi tiết

#### 4.12.1 Test thật

```bash
curl -X POST "https://192.168.100.60:8443/forms/fresher-demo/patient-review-form/submit" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "answers": [
      { "fieldKey": "ket_luan", "value": "Đạt tiêu chuẩn" },
      { "fieldKey": "ghi_chu",  "value": "Đã kiểm tra hồ sơ, hội chẩn xong, đồng ý chuyển khoa hồi sức." }
    ]
  }'
```

**Response (HTTP 200):**

```json
{
  "success": true,
  "data": {
    "submissionId": "e7fc775e-8f89-4258-865a-a5d65d2e33e3"
  }
}
```

#### 4.12.2 Handler logic

```
1. Fetch form template
   template = await templates.GetByModuleAndKeyAsync(moduleCode, formKey, false)
   if (template == null) return NotFound

2. Guard: phải Published
   if (template.Status != Published)
     return Failure(Conflict("Form is not published"))

3. Convert input → domain
   answers = request.Answers
     .Select(a => new FieldAnswer(a.FieldKey, a.Value))
     .ToList()

4. Create submission
   submission = FormSubmission.Create(
     template.Id, template.ModuleCode, template.Key,
     template.Version,    // ← snapshot
     submittedBy: userIdFromJwt,
     answers)

5. Save
   await submissions.AddAsync(submission)
   await uow.SaveChangesAsync()

6. Publish integration event
   await eventBus.PublishAsync(
     new FormSubmittedIntegrationEvent(submission.Id, ...))
     // → RabbitMQ → NotificationService có thể subscribe để báo cho ai cần

7. Return SubmitFormResultDto(submission.Id)
```

#### 4.12.3 Verify submission lưu đúng

```bash
curl "https://192.168.100.60:8443/forms/admin/forms/7f87c94c-7772-4816-a709-d0fe68f1143d/submissions" \
  -H "Authorization: Bearer $TOKEN"
```

**Response (test thật):**

```json
{
  "success": true,
  "data": [
    {
      "id": "e7fc775e-8f89-4258-865a-a5d65d2e33e3",
      "moduleCode": "fresher-demo",
      "formKey": "patient-review-form",
      "formVersion": 2,                        ← snapshot version
      "submittedBy": null,
      "status": "Submitted",
      "submittedAt": "2026-06-04T03:35:12.719867Z",
      "answers": [
        { "FieldKey": "ket_luan", "Value": "Đạt tiêu chuẩn" },
        { "FieldKey": "ghi_chu",  "Value": "Đã kiểm tra hồ sơ, hội chẩn xong, đồng ý chuyển khoa hồi sức." }
      ]
    }
  ]
}
```

**Quan sát quan trọng:**
- `formVersion: 2` ← version form lúc submit
- `answers` chỉ chứa **2 field** (`ket_luan`, `ghi_chu`) — không có `hoten`, `ngaysinh`...
- Tại sao? Vì 6 field bound là `isReadOnly=true` — frontend không gửi lên. Xem lý do ở phần [8.5](#85-tại-sao-submit-chỉ-gửi-field-user-nhập).

---

## 5. Expression Binding

### 5.1 Khái niệm cốt lõi

Expression binding là **cầu nối** giữa data trong DataMatching và field trong DynamicForm. Field không hardcode giá trị, mà chứa một "công thức" (expression) chỉ tới đâu lấy giá trị.

```
field.dataBinding.expression = "{{sources.record.HoTen}}"
                                    ↑        ↑       ↑
                                 keyword  namespace  path

- "sources"  → cố định, luôn là sources
- "record"   → namespace của DataSource (admin đặt)
- "HoTen"    → key trong canonicalPayload
```

### 5.2 DataSource — Khai báo "sẽ fetch từ đâu"

DataSource được khai báo ở cấp **Screen**, không phải field. Vì 1 screen chỉ fetch 1 (hoặc vài) source, tất cả field dùng chung.

```json
{
  "namespace":      "record",
  "serviceId":      "datamatch",
  "resourcePath":   "/dm/records/{recordId}",
  "requiredParams": ["recordId"]
}
```

| Field | Ý nghĩa |
|---|---|
| `namespace` | Tên biến trong `sources` mà expression dùng. Phải unique trong screen. |
| `serviceId` | ID logic của service. Frontend dùng để resolve base URL (vd: `datamatch` → `https://server/dm`). |
| `resourcePath` | URL endpoint, có placeholder `{paramName}`. |
| `requiredParams` | Frontend phải lấy đủ các params này từ URL hoặc context, nếu thiếu thì không fetch. |

### 5.3 Frontend evaluate — Step by step

Giả sử URL hiện tại là `/review/patients/abc-123`.

```
BƯỚC 1: Fetch layout từ DynamicFormService
        ──────────────────────────────────────
GET /forms/screens/fresher-demo/patient-review/layout
                                ↓
            Nhận về JSON gồm:
              dataSources: [{ namespace, resourcePath, requiredParams }]
              tabs[].widgets[].formSchema.fields[].dataBinding


BƯỚC 2: Đọc dataSources, extract params từ URL
        ─────────────────────────────────────────
For each ds in dataSources:
  requiredParams = ["recordId"]
  
  URL hiện tại: /review/patients/abc-123
                 → match route pattern: /review/patients/:recordId
                 → extract: recordId = "abc-123"
  
  Build URL thực tế: ds.resourcePath = "/dm/records/{recordId}"
                                       "/dm/records/abc-123"
  
  → Fetch baseUrlOf(ds.serviceId) + resolvedPath


BƯỚC 3: Lưu kết quả vào biến sources
        ──────────────────────────────
For each ds → fetch response.json()
  sources[ds.namespace] = responseBody.data

Ví dụ:
sources["record"] = {
  id: "abc-123",
  sourceSystem: "his-fresher",
  recordType: "benh-nhan",
  canonicalPayload: "{\"HoTen\":\"Phạm Quỳnh Như\",\"TenKhoa\":\"Khoa Tim Mạch\",...}"
}

Để dễ dùng expression `{{sources.record.HoTen}}`, FE thường parse
canonicalPayload thêm 1 lần:
sources["record"] = {
  ...originalResponse,
  ...JSON.parse(canonicalPayload)   // ← spread vào trực tiếp
}


BƯỚC 4: Với mỗi field, evaluate expression
        ────────────────────────────────────
For each field:
  if (field.dataBinding != null) {
    expression = field.dataBinding.expression
                 "{{sources.record.HoTen}}"
    
    value = evaluate(expression, sources)
            ↓
            "Phạm Quỳnh Như"
    
    if (field.dataBinding.displayFormat) {
      value = format(value, field.dataBinding.displayFormat)
              // "1992-08-14" + "date:DD/MM/YYYY" → "14/08/1992"
    }
    
    initialValue[field.key] = value
  } else {
    initialValue[field.key] = ""   // free field, user tự nhập
  }


BƯỚC 5: Render form
        ─────────────
For each field:
  if (field.isReadOnly):
    render <input value={initialValue[key]} disabled />
  else:
    render <input value={initialValue[key]} onChange={...} />
```

### 5.4 Code mẫu evaluator (TypeScript)

```typescript
// Mustache-style expression evaluator (~40 dòng)

type Sources = Record<string, any>;

// Lấy giá trị theo path "a.b.c"
function resolvePath(obj: any, path: string): any {
  const keys = path.split('.');
  let cur = obj;
  for (const k of keys) {
    if (cur == null) return undefined;
    cur = cur[k];
  }
  return cur;
}

// Parse "{{sources.record.HoTen}}" → "Phạm Quỳnh Như"
export function evaluate(expression: string, sources: Sources): string {
  // Match {{ ... }}
  const re = /\{\{\s*sources\.([\w.]+)\s*\}\}/g;
  return expression.replace(re, (_, path) => {
    const val = resolvePath(sources, path);
    return val == null ? '' : String(val);
  });
}

// Format value theo displayFormat
export function formatValue(val: string, format?: string | null): string {
  if (!format || !val) return val;
  if (format.startsWith('date:')) {
    const pattern = format.slice(5);   // "DD/MM/YYYY"
    const d = new Date(val);
    if (isNaN(d.getTime())) return val;
    return pattern
      .replace('DD',   String(d.getDate()).padStart(2, '0'))
      .replace('MM',   String(d.getMonth() + 1).padStart(2, '0'))
      .replace('YYYY', String(d.getFullYear()));
  }
  if (format === 'currency:VND') {
    const n = Number(val);
    return isNaN(n) ? val : n.toLocaleString('vi-VN') + ' ₫';
  }
  return val;
}

// Demo
const sources = {
  record: {
    HoTen: 'Phạm Quỳnh Như',
    NgaySinh: '1992-08-14',
  }
};

console.log(evaluate('{{sources.record.HoTen}}', sources));
// → "Phạm Quỳnh Như"

console.log(formatValue(
  evaluate('{{sources.record.NgaySinh}}', sources),
  'date:DD/MM/YYYY'
));
// → "14/08/1992"
```

### 5.5 Cú pháp expression đầy đủ

| Expression | Ý nghĩa |
|---|---|
| `{{sources.record.HoTen}}` | Field `HoTen` từ source `record` |
| `{{sources.record.address.city}}` | Nested: `record.address.city` |
| `{{sources.patients}}` | Toàn bộ array (dùng cho Table widget) |
| `{{sources.report.rows}}` | Array rows từ report |

**displayFormat** — hint cho frontend format:

| Format | Input | Output |
|---|---|---|
| `date:DD/MM/YYYY` | `"1992-08-14"` | `"14/08/1992"` |
| `date:MM/YYYY` | `"1992-08-14"` | `"08/1992"` |
| `currency:VND` | `150000` | `"150.000 ₫"` |
| `null` | `"x"` | `"x"` (giữ nguyên) |

---

## 6. Full luồng

### 6.0 Authentication

Tất cả endpoint admin nên yêu cầu JWT. (Hiện tại một số endpoint đã commented out `[Authorize]` để demo dễ — production cần bật lại.)

#### Login

```bash
# Lần đầu (đăng ký user)
curl -X POST "https://192.168.100.60:8443/auth/register" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "freshertest@hdos.local",
    "password": "Fresher@123",
    "fullName": "Fresher Tester"
  }'

# Login (lần sau)
curl -X POST "https://192.168.100.60:8443/auth/login" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "freshertest@hdos.local",
    "password": "Fresher@123"
  }'
```

**Response:**
```json
{
  "success": true,
  "data": {
    "userId": "980ef315-a793-4a33-b7d9-3163444e22be",
    "email": "freshertest@hdos.local",
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...VBIT9gGXQiTo43SyvIfrtDs6NcBn6p0YjDNN_3SPZ7Y"
  }
}
```

#### Gắn JWT vào mọi request

```bash
TOKEN="eyJhbGc..."

curl "https://server/forms/modules" \
  -H "Authorization: Bearer $TOKEN"
```

JWT có hạn (default 8h). Hết hạn → 401 → login lại.

### 6.1 Admin setup (làm 1 lần)

```
VIỆC 1 — Đăng ký SourceProfile trong DataMatchingService
─────────────────────────────────────────────────────────
POST /dm/sources
{
  "sourceSystem":     "his-fresher",
  "recordType":       "benh-nhan",
  "displayName":      "HIS Fresher Demo - Benh nhan",
  "businessKeyField": "MaBenhNhan",
  "mappings": {
    "ma_bn":     "MaBenhNhan",
    "ho_ten":    "HoTen",
    "ngay_sinh": "NgaySinh",
    "ten_khoa":  "TenKhoa",
    "so_giuong": "SoGiuong",
    "chan_doan": "ChanDoan",
    "bac_si":    "BacSiPhuTrach"
  }
}


VIỆC 2 — Auto-generate form trong DynamicFormService (1 lệnh)
──────────────────────────────────────────────────────────────
POST /forms/admin/generate-from-source
{
  "moduleCode":  "fresher-demo",
  "screenCode":  "patient-review",
  "formKey":     "patient-review-form",
  "dataSource": {
    "namespace":      "record",
    "resourcePath":   "/dm/records/{recordId}",
    "requiredParams": ["recordId"]
  },
  "fields": [
    { "canonicalKey": "HoTen",    "label": "Họ tên" },
    { "canonicalKey": "TenKhoa",  "label": "Khoa" },
    { "canonicalKey": null, "fieldKey": "ket_luan", "label": "Kết luận", "fieldType": "Select", "options": ["Đạt","Chưa đạt"], "isReadOnly": false }
  ]
}

→ Hệ thống tự tạo:
  ① Module      "fresher-demo"
  ② Screen      "patient-review" + DataSources
  ③ Form        "patient-review-form" v2 + fields (bound + free)
  ④ Tab "main"  + Widget "FormSection" → form.Id
  ⑤ Publish     cả screen lẫn form
```

### 6.2 Dữ liệu vào hệ thống (mỗi ngày)

```
HIS bệnh viện gửi data:
─────────────────────────
POST /dm/ingest/json
{
  "sourceSystem": "his-fresher",
  "recordType":   "benh-nhan",
  "payload": {
    "ma_bn":     "BN-FRESH-001",
    "ho_ten":    "Phạm Quỳnh Như",
    "ngay_sinh": "1992-08-14",
    "ten_khoa":  "Khoa Tim Mạch",
    "so_giuong": "TM-12",
    "chan_doan": "Rối loạn nhịp tim, theo dõi 24h",
    "bac_si":    "BS. Trần Văn Đạt"
  }
}

→ Xử lý:
  ① Map field:   ho_ten → HoTen, ten_khoa → TenKhoa, ...
  ② Hash check:  payload mới → lưu
  ③ Lưu DB:     status = Pending
  ④ ~30s sau:   Worker chạy → status = Matched

→ Record sẵn sàng, ID = "e386531f-786d-4efa-86d8-e876dd200f14"
```

### 6.3 Bác sĩ mở form xét duyệt — Sequence diagram đầy đủ

```
Bác sĩ           Browser              DynamicForm      DataMatching        Postgres
  │                │                       │                │                  │
  │ Click hồ sơ    │                       │                │                  │
  │ ───────────────►                       │                │                  │
  │                │                       │                │                  │
  │                │ URL → /review/patients/e386531f...     │                  │
  │                │                       │                │                  │
  │                │ GET /forms/screens/fresher-demo/       │                  │
  │                │     patient-review/layout              │                  │
  │                │ ─────────────────────►│                │                  │
  │                │                       │                │                  │
  │                │                       │ Query screen + tabs + widgets    │
  │                │                       │ + dataSources                     │
  │                │                       │ ──────────────────────────────► │
  │                │                       │                │                  │
  │                │                       │ ◄──────────────────────────────── │
  │                │                       │                │                  │
  │                │ ◄─────────────────────│                │                  │
  │                │ {dataSources:[{namespace:"record",     │                  │
  │                │  resourcePath:"/dm/records/{recordId}",│                  │
  │                │  requiredParams:["recordId"]}],        │                  │
  │                │  tabs:[{widgets:[{formSchema:{...}}]}]}                  │
  │                │                       │                │                  │
  │                │  ──── Extract recordId từ URL ────     │                  │
  │                │  ──── = "e386531f-786d-4efa-..."  ──── │                  │
  │                │                       │                │                  │
  │                │ GET /dm/records/e386531f...            │                  │
  │                │ ────────────────────────────────────► │                  │
  │                │                       │                │                  │
  │                │                       │                │ SELECT ...       │
  │                │                       │                │ ────────────────►│
  │                │                       │                │ ◄────────────────│
  │                │                       │                │                  │
  │                │ ◄────────────────────────────────────  │                  │
  │                │ {canonicalPayload:"{\"HoTen\":\"...\",...}"}             │
  │                │                       │                │                  │
  │                │  ──── Parse canonicalPayload ──────    │                  │
  │                │  ──── sources["record"] = {            │                  │
  │                │       HoTen: "Phạm Quỳnh Như",         │                  │
  │                │       NgaySinh: "1992-08-14", ...   }  │                  │
  │                │                       │                │                  │
  │                │  ──── For each field: evaluate ──────  │                  │
  │                │   {{sources.record.HoTen}} → "Phạm Quỳnh Như"            │
  │                │   {{sources.record.NgaySinh}} + date format → "14/08/1992"
  │                │                       │                │                  │
  │ ◄──────────────│                       │                │                  │
  │ Form render:   │                       │                │                  │
  │ ┌─────────────┐│                       │                │                  │
  │ │ Họ tên: ... │ (pre-filled, disabled) │                │                  │
  │ │ Ngày sinh:..│                        │                │                  │
  │ │ ...         │                        │                │                  │
  │ │ Kết luận: ▼ │ (empty, user nhập)     │                │                  │
  │ │ Ghi chú: [] │                        │                │                  │
  │ └─────────────┘                        │                │                  │
```

### 6.4 Bác sĩ điền và submit

```
Bác sĩ chọn:  Kết luận = "Đạt tiêu chuẩn"
Bác sĩ gõ:    Ghi chú = "Đã kiểm tra, hội chẩn xong, đồng ý chuyển khoa hồi sức."
Bấm "Gửi xét duyệt"

Frontend:
  Lọc chỉ field user nhập (filter !isReadOnly):
  payload = {
    answers: [
      { fieldKey: "ket_luan", value: "Đạt tiêu chuẩn" },
      { fieldKey: "ghi_chu",  value: "Đã kiểm tra..." }
    ]
  }

POST /forms/fresher-demo/patient-review-form/submit
  body = payload
  header.Authorization = "Bearer <jwt>"

← Response: { submissionId: "e7fc775e-8f89-4258-865a-a5d65d2e33e3" }

DynamicFormService lưu:
  FormSubmission {
    id:             "e7fc775e-...",
    formTemplateId: "7f87c94c-...",
    moduleCode:     "fresher-demo",
    formKey:        "patient-review-form",
    formVersion:    2,                            ← snapshot
    submittedBy:    "980ef315-..." (từ JWT),
    status:         Submitted,
    answers: [
      { fieldKey: "ket_luan", value: "Đạt tiêu chuẩn" },
      { fieldKey: "ghi_chu",  value: "Đã kiểm tra..." }
    ],
    submittedAt: 2026-06-04T03:35:12.719867Z
  }

Frontend hiển thị: settings.successMessage = "Đã gửi form thành công"
```

**Lưu ý:** Không lưu `HoTen`, `NgaySinh`, `TenKhoa`... vào submission. Lý do giải thích ở [8.5](#85-tại-sao-submit-chỉ-gửi-field-user-nhập).

---

## 7. Widget và Dashboard

### 7.1 Nguyên lý chung

DataSources fetch vào biến `sources` — **mọi widget trên cùng screen đều đọc được**. Một screen có thể khai báo nhiều DataSource:

```json
{
  "dataSources": [
    { "namespace": "record",  "resourcePath": "/dm/records/{recordId}",          "requiredParams": ["recordId"] },
    { "namespace": "ward",    "resourcePath": "/dm/records?recordType=noi-tru",   "requiredParams": [] },
    { "namespace": "report",  "resourcePath": "/dm/reports/chi-phi-theo-khoa",    "requiredParams": [] }
  ]
}
```

Frontend fetch song song 3 endpoint, lưu vào sources:

```js
sources["record"] = { TenBenhNhan: "...", TenKhoa: "..." }
sources["ward"]   = [{...}, {...}, {...}]
sources["report"] = { rows: [{ TenKhoa: "Tim Mạch", ChiPhi: 150000 }] }
```

### 7.2 KPI Card widget

Admin config trong `ConfigJson`:

```json
{
  "title":           "Khoa điều trị",
  "valueExpression": "{{sources.record.TenKhoa}}",
  "unit":            "",
  "color":           "#6366f1"
}
```

Frontend evaluate `valueExpression`:

```
sources["record"]["TenKhoa"] = "Khoa Tim Mạch"

Render:
┌──────────────────────┐
│  Khoa điều trị       │
│                      │
│  Khoa Tim Mạch       │
└──────────────────────┘
```

### 7.3 Table widget

```json
{
  "dataExpression": "{{sources.ward}}",
  "columns": [
    { "field": "TenBenhNhan", "header": "Họ tên" },
    { "field": "TenKhoa",     "header": "Khoa" },
    { "field": "TrangThai",   "header": "Trạng thái" }
  ]
}
```

Frontend evaluate dataExpression:

```
sources["ward"] = [
  { TenBenhNhan: "Nguyễn Văn An", TenKhoa: "Tim Mạch", TrangThai: "Đang nội trú" },
  { TenBenhNhan: "Trần Thị Bình", TenKhoa: "Nhi Khoa",  TrangThai: "Đã xuất viện" }
]

Render:
┌──────────────────┬────────────┬────────────────┐
│ Họ tên           │ Khoa       │ Trạng thái     │
├──────────────────┼────────────┼────────────────┤
│ Nguyễn Văn An    │ Tim Mạch   │ Đang nội trú   │
│ Trần Thị Bình    │ Nhi Khoa   │ Đã xuất viện   │
└──────────────────┴────────────┴────────────────┘
```

### 7.4 Chart widget

```json
{
  "chartType":      "pie",
  "dataExpression": "{{sources.report.rows}}",
  "labelField":     "TenKhoa",
  "valueField":     "SoBenhNhan"
}
```

```
sources["report"]["rows"] = [
  { TenKhoa: "Tim Mạch", SoBenhNhan: 12 },
  { TenKhoa: "ICU",      SoBenhNhan: 5 },
  { TenKhoa: "Nhi",      SoBenhNhan: 20 }
]

→ Render pie chart với 3 slices
```

---

## 8. Tư duy thiết kế

### 8.1 Tại sao không lưu data trong DynamicFormService?

**Câu hỏi:** Sao không copy data từ DataMatching sang DynamicForm 1 lần cho tiện?

```
Nếu copy:
  - 8h: bác sĩ nhập viện → form hiển thị TenKhoa = "Tim Mạch"  (đã copy)
  - 9h: bệnh nhân chuyển sang ICU (DataMatching update)
  - 10h: bác sĩ khác mở form → vẫn thấy "Tim Mạch" (sai!)
  → Trong y tế = nguy hiểm

Nếu fetch mỗi lần mở:
  - Luôn lấy data mới nhất từ nguồn gốc
  - DataMatching là single source of truth
  → Luôn đúng
```

### 8.2 Tại sao dùng expression thay vì hardcode?

```
Hardcode:
  field "ho_ten" → CODE: lấy từ canonicalPayload["HoTen"]
  → Chỉ dùng được cho DataMatching
  → Muốn dùng OrderService → phải code lại

Expression:
  field "ho_ten" → CONFIG: "{{sources.patient.fullName}}"
  → DataSource "patient" trỏ vào BẤT KỲ service nào
  → Hôm nay: /dm/records/{id}
  → Mai: /m01/patients/{id}
  → Không sửa code, chỉ đổi config
```

### 8.3 Tại sao generate-from-source làm 1 lệnh?

```
Từng bước:
  8 API call, dễ nhầm thứ tự, dễ quên
  Bước 5 lỗi → phải rollback thủ công

generate-from-source:
  1 API call duy nhất
  Atomic: thành công hết hoặc rollback hết
  Phù hợp khi biết sẵn schema (có SourceProfile rồi)

Cả 2 đều cần:
  - Tùy chỉnh phức tạp → dùng từng bước qua UI designer
  - Nhanh từ schema sẵn → dùng generate-from-source
```

### 8.4 Tại sao DataSource ở Screen level, không phải Field level?

```
Field-level DataSource:
  field "HoTen"     → fetch /dm/records/abc
  field "TenKhoa"   → fetch /dm/records/abc  ← gọi lại!
  field "ChanDoan"  → fetch /dm/records/abc  ← gọi lại!
  → 10 bound field = 10 HTTP call → chậm, lãng phí

Screen-level DataSource:
  Screen khai báo: fetch /dm/records/abc → lưu vào "record"
  Tất cả field đọc sources["record"] → chỉ 1 HTTP call
  → Hiệu quả
```

### 8.5 Tại sao submit chỉ gửi field user nhập?

```
Gửi hết (bao gồm field bound):
  { HoTen: "Phạm Quỳnh Như", TenKhoa: "Tim Mạch", ket_luan: "Đạt" }
  → Lưu HoTen, TenKhoa vào FormSubmission (duplicate với DataMatching)
  → Nếu DataMatching cập nhật → submission cũ stale
  → Waste storage

Chỉ gửi field user nhập:
  { ket_luan: "Đạt", ghi_chu: "..." }
  → FormSubmission chỉ chứa ý kiến của bác sĩ
  → Đọc lại: ghép submission + record (qua submissionMetadata.recordId nếu cần)
  → Data luôn đồng bộ
```

### 8.6 Tại sao tách 4 tầng Clean Architecture?

```
Project structure 4 tầng (Domain/Application/Infrastructure/API):
  ── Domain pure C# → test bằng unit test, không cần DB
  ── Đổi Postgres sang MongoDB → chỉ sửa Infrastructure
  ── Đổi REST sang gRPC → chỉ sửa API
  ── Logic nghiệp vụ tập trung Domain + Application → dễ tìm/sửa

  Trade-off: nhiều file hơn, cần học pattern
  Lợi ích: maintainability lâu dài
  Phù hợp dự án ≥ 6 tháng
```

### 8.7 Tại sao cần Versioning FormTemplate?

```
Không version:
  Form "v1" có field "diem_so" type Number
  3 tháng sau: đổi thành type Text (vì có giá trị "N/A")
  Đọc lại submission cũ: "diem_so": "85" → hiển thị OK
  Đọc submission rất cũ: "diem_so": 85 (number) → có thể parse được
  → Hên xui

Có version:
  Submission v1 → biết là Number, render input number
  Submission v2 → biết là Text, render input text
  → Diễn giải chính xác theo bối cảnh lúc submit
  → Audit/compliance dễ
```

---

## 9. Checklist & Troubleshooting

### 9.1 Thêm loại form mới

```
□ Đăng ký SourceProfile nếu sourceSystem mới:
  POST /dm/sources

□ Auto-generate form:
  POST /forms/admin/generate-from-source

□ Verify layout:
  GET /forms/screens/{module}/{screen}/layout
  → Kiểm tra dataSources + expressions

□ Test evaluate thủ công:
  GET /dm/records/{id} → lấy canonicalPayload
  Tự map xem đúng không

□ Test submit:
  POST /forms/{module}/{formKey}/submit
```

### 9.2 Thêm widget dashboard mới

```
□ Xác định DataSource cung cấp data:
  - 1 record: /dm/records/{id}
  - List: /dm/records?recordType=...
  - Report: /dm/reports/{code}

□ Thêm DataSource vào screen (nếu chưa có):
  PUT /forms/admin/screens/{m}/{s}/data-sources

□ Config widget ConfigJson với expressions:
  { "valueExpression": "{{sources.namespace.path}}" }

□ Frontend implement resolver cho loại widget mới
```

### 9.3 Troubleshooting đầy đủ

#### A. `401 Unauthorized`

```
Triệu chứng: { "errorCode": "Unauthorized" }
Nguyên nhân: JWT hết hạn hoặc không gửi
Fix:
  curl -X POST .../auth/login → lấy token mới
  Header: Authorization: Bearer <token>
```

#### B. `404 NotFound` — Record không tìm thấy

```bash
# Test thật:
curl ".../dm/records/00000000-0000-0000-0000-000000000000" -H "Authorization: Bearer $TOKEN"
```

```json
{
  "success": false,
  "errorCode": "NotFound",
  "errorMessage": "Record '00000000-0000-0000-0000-000000000000' was not found"
}
```

**Nguyên nhân:**
- ID sai
- Record vẫn `Pending`, Worker chưa xử lý → có thể đọc được nhưng status chưa Matched (record vẫn lấy được, chỉ là chưa Matched)
- Đã xóa

**Fix:** Kiểm tra ID, đợi 30s nếu vừa ingest.

#### C. `404 NotFound` — SourceProfile chưa đăng ký

```bash
# Test thật:
curl -X POST .../dm/ingest/json -d '{"sourceSystem":"chua-ton-tai","recordType":"x","payload":{...}}'
```

```json
{
  "success": false,
  "errorCode": "NotFound",
  "errorMessage": "SourceProfile 'chua-ton-tai/x' not found. was not found"
}
```

**Fix:** Đăng ký SourceProfile trước bằng `POST /dm/sources`.

#### D. `409 Conflict` — Payload trùng hash

```json
{
  "success": false,
  "errorCode": "Conflict",
  "errorMessage": "Duplicate payload: a record with this exact content already exists."
}
```

**Nguyên nhân:** Đúng JSON đã ingest trước đó. SHA-256 hash trùng.

**Fix:**
- Nếu thực sự là duplicate → không cần làm gì, đã có rồi
- Nếu là update → ingest payload mới (sửa 1 field bất kỳ là hash đổi)

#### E. `409 Conflict` — Screen/Form đã tồn tại

```bash
# Test thật:
curl -X POST .../forms/admin/generate-from-source -d '{"moduleCode":"fresher-demo","screenCode":"patient-review",...}'
```

```json
{
  "success": false,
  "errorCode": "Conflict",
  "errorMessage": "Screen 'patient-review' đã tồn tại trong module 'fresher-demo'."
}
```

**Fix:** Dùng screenCode khác, hoặc xóa screen cũ trước:
```bash
DELETE /forms/admin/screens/fresher-demo/patient-review
```

#### F. Expression không evaluate ra giá trị

**Triệu chứng:** Field hiển thị `{{sources.record.HoTen}}` literal hoặc rỗng.

**Debug:**

```js
// 1. Log sources sau khi fetch
console.log('sources:', sources);
// Kiểm tra: sources.record có đúng namespace không?

// 2. Kiểm tra canonicalPayload
fetch('/dm/records/' + recordId)
  .then(r => r.json())
  .then(d => {
    console.log('Canonical:', JSON.parse(d.data.canonicalPayload));
    // → field "HoTen" có thật không? (chú ý hoa thường!)
  });

// 3. Kiểm tra SourceProfile mappings
fetch('/dm/sources?sourceSystem=his-fresher')
  .then(r => r.json())
  .then(d => console.log('Mappings:', d.data[0].mappings));
  // → "ho_ten" có map thành "HoTen" không?

// 4. Kiểm tra syntax expression
// ✓ Đúng:  {{sources.record.HoTen}}
// ✗ Sai:   {{ sources.record.HoTen }}      (có khoảng trắng — vẫn match được nếu regex tolerant)
// ✗ Sai:   {{Sources.record.HoTen}}        (Sources viết hoa)
// ✗ Sai:   {{sources.record.hoten}}        (key lowercase — JSON case-sensitive)
```

#### G. Form không hiển thị ở frontend

**Triệu chứng:** Layout response không chứa form schema.

**Nguyên nhân thường gặp:**
- Form status = `Draft` → chưa Publish
- Screen status = `Draft` → chưa Publish
- Widget có `WidgetType=FormSection` nhưng `ReferenceId = null` hoặc trỏ tới formId không tồn tại

**Fix:**
```bash
POST /forms/admin/forms/{formId}/publish
POST /forms/admin/screens/{moduleCode}/{screenCode}/publish
```

#### H. Search `GET /dm/records?field=&value=` không trả gì

**Nguyên nhân:**
- Case-sensitive: `value=Tim+Mach` (không dấu) không khớp `Tim Mạch` (có dấu)
- Field name sai: dùng tên gốc (`ho_ten`) thay vì canonical (`HoTen`)
- Record vẫn `Pending` → vẫn được trả về (search không filter status), nhưng có thể frontend filter ra

**Fix:** Dùng đúng case + canonical name. Kiểm tra trực tiếp:
```bash
psql "Host=localhost;Port=5433;Username=postgres;Password=postgres;Database=DataMatchingDb" \
  -c "SELECT \"CanonicalPayload\" FROM \"StagingRecords\" LIMIT 1;"
```

#### I. MatchingWorker không xử lý record

**Triệu chứng:** Sau >30s, record vẫn `Pending`.

**Debug:**
```bash
# Xem log
docker compose logs -f datamatchingservice | grep -i matchingworker

# Có thể thấy:
# "MatchingWorker started. Interval: 30s"
# "MatchingWorker processed 1 records in this batch."
```

**Nguyên nhân:**
- Service down → restart `docker compose restart datamatchingservice`
- Worker bị exception loop → check log Warning
- BackgroundService chưa register → kiểm tra Program.cs

---

## 10. Tóm tắt 1 trang

```
┌─────────────────────────────────────────────────────────────────────┐
│                    LUỒNG HOÀN CHỈNH                                 │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  SETUP (admin làm 1 lần):                                           │
│  1. POST /auth/login                  → JWT                         │
│  2. POST /dm/sources                  → SourceProfile               │
│  3. POST /forms/admin/generate-from-source → Module+Screen+Form+... │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  INGEST (HIS làm mỗi ngày):                                         │
│  4. POST /dm/ingest/json              → StagingRecord (Pending)     │
│  5. MatchingWorker (~30s)             → Pending → Matched           │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  RUNTIME (mỗi lần user mở form):                                    │
│  6. GET /forms/screens/{m}/{s}/layout                               │
│     ← nhận dataSources + fields với expressions                     │
│  7. GET /dm/records/{id}                                            │
│     ← nhận canonicalPayload                                         │
│  8. Frontend evaluate {{sources.record.HoTen}} → "Phạm Quỳnh Như"   │
│  9. Render form pre-filled                                          │
│  10.POST /forms/{m}/{f}/submit                                      │
│     ← chỉ gửi field user nhập, server tự gắn FormVersion snapshot   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘

KEY PRINCIPLES:
  ✓ Zero coupling — 2 service không gọi nhau, FE là cầu nối
  ✓ Single source of truth — data gốc ở DataMatching, không copy
  ✓ Expression binding — config thay đổi, không sửa code
  ✓ Screen-level DataSources — fetch 1 lần, dùng nhiều
  ✓ Submit chỉ field user nhập — không duplicate data
  ✓ Versioning — audit chính xác theo bối cảnh
  ✓ Clean Architecture — Domain pure, dễ test/đổi
  ✓ SHA-256 dedup + JSONB + GIN index — performance ổn định
  ✓ Background Worker — xử lý ngầm, không block ingest
  ✓ generate-from-source — 1 lệnh = toàn bộ setup

DOMAIN MODEL:
  DataMatching:  SourceProfile (1) ─── (N) StagingRecord
  DynamicForm:   FormModule (1) ─── (N) FormTemplate ─── (N) FormField
                            (1) ─── (N) FormScreen   ─── (N) FormScreenTab
                                                         ─── (N) FormScreenWidget
                            (independent) FormSubmission, WidgetCatalog

CLEAN ARCHITECTURE (mỗi service):
  Domain         → Entity, ValueObject, Enum, IRepository (pure C#)
  Application    → Command, Query, Handler, Validator, DTO
  Infrastructure → DbContext, Repository impl, Worker, MassTransit
  API            → Controller, Program.cs, DI, Middleware

STATUS LIFECYCLE:
  Draft  → Published → Archived
  (sửa)    (read-only)  (ẩn)

ENDPOINTS BIẾT NHIỀU NHẤT:
  DataMatching:  POST /dm/sources       /dm/ingest/json       GET /dm/records/{id}
  DynamicForm:   POST /forms/admin/generate-from-source
                 GET  /forms/screens/{m}/{s}/layout
                 POST /forms/{m}/{f}/submit
                 GET  /forms/{m}/{f}/schema  (BDUI)
```
