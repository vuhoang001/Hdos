# 24 — Dashboard Engine, DataMatchingService & SSE Push

Hướng dẫn đầy đủ: kiến trúc, luồng dữ liệu từ HIS vào đến dashboard,
cách thêm dashboard mới, SSE push realtime, và cách test.

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
    ├─ GET /dm/dashboards/{code}
    │   DashboardEngine tìm config theo code
    │   → Fetch StagingRecord (Status=Matched) song song theo RecordTypes
    │   → Parse CanonicalPayload → gọi config.BuildSections()
    │   → Trả sections[]
    │
    └─ MatchingWorker xử lý xong batch
        → Publish DashboardFeReadyIntegrationEvent (via Outbox)
        → RabbitMQ → NotificationService
        → SSE broadcast xuống Frontend
    │
    ▼
[Frontend Next.js]
    <DashboardRenderer sections={sections} />      ← REST polling
    EventSource("/notifications/sse")              ← SSE realtime refresh
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
    "ma_bn": "BN26000004", "ho_ten": "Phạm Thị Dung",
    "ten_khoa": "Sản khoa", "so_giuong": "SAN-04",
    "ngay_nhap": "2026-05-25", "ngay_xuat": "2026-05-28",
    "doi_tuong": "BHYT", "trang_thai": "DaXuatVien",
    "ma_icd": "Z39.0", "ten_icd": "Hậu sản bình thường", "chan_doan": "Sau sinh thường"
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

### Bước 3 — Ingest cấu hình giường và bệnh nhân

```http
POST http://localhost:5004/dm/ingest/file
Content-Type: multipart/form-data

sourceSystem: his-01
recordType:   cau-hinh-giuong
file:         giuong.json
```

### Bước 4 — Chờ MatchingWorker xử lý

MatchingWorker chạy nền mỗi 30 giây, tự động chuyển record từ `Pending` → `Matched`.

```http
GET http://localhost:5004/dm/records?sourceSystem=his-01&recordType=benh-nhan-noi-tru
```

### Bước 5 — Gọi dashboard

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
          { "label": "BOR",              "value": 11.3,"unit": "%",        "format": "percent" },
          { "label": "Vào viện hôm nay", "value": 4,  "unit": "lượt",     "format": "number"  }
        ]
      },
      {
        "type": "pie-chart", "id": "doi-tuong-kcb", "title": "Phân loại đối tượng KCB",
        "data": [
          { "label": "BHYT", "soLuong": 10, "phanTram": 76.9 }
        ]
      },
      {
        "type": "bar-chart", "id": "top-icd", "title": "Top 10 ICD hôm nay",
        "data": [
          { "label": "Viêm phổi, không xác định", "soLuong": 3 }
        ]
      },
      {
        "type": "table", "id": "danh-sach-benh-nhan", "title": "Danh sách bệnh nhân nội trú",
        "columns": [
          { "key": "mrn", "label": "MRN", "type": "string" },
          { "key": "tenKhoa", "label": "Khoa", "type": "string" }
        ],
        "rows": [ { "mrn": "BN26000001", "tenKhoa": "Nội tổng hợp" } ]
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
  "sourceSystem": "his-01", "recordType": "phau-thuat",
  "displayName": "HIS - Phẫu thuật", "businessKeyField": "MRN",
  "mappings": { "ma_bn": "MRN", "ten_pt": "TenPhauThuat", "ngay_pt": "NgayPhauThuat",
                "bac_si": "BacSiPhauThuat", "loai_pt": "LoaiPhauThuat", "ket_qua": "KetQua" }
}
```

### Bước 2 — Tạo file config

`DataMatchingService.Application/Dashboard/Configs/M03DashboardConfig.cs`

```csharp
public sealed class M03DashboardConfig : DashboardConfig
{
    public override string Code  => "m03";
    public override string Title => "Báo cáo Phẫu thuật";
    public override IReadOnlyList<string> RecordTypes => ["phau-thuat"];

    public override List<DashboardSection> BuildSections(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data, DateOnly reportDate)
    {
        var rows = data.GetValueOrDefault("phau-thuat", []);
        return [BuildKpi(rows, reportDate), BuildLoaiPt(rows), BuildTable(rows)];
    }
    // ... BuildKpi, BuildLoaiPt, BuildTable helpers
}
```

### Bước 3 — Đăng ký DI (1 dòng)

```csharp
services.AddSingleton<DashboardConfig, M03DashboardConfig>(); // ← thêm dòng này
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

| Canonical field | HIS field | Bắt buộc |
|-----------------|-----------|----------|
| `MRN` | `ma_bn` | Có |
| `TenBenhNhan` | `ho_ten` | Có |
| `TenKhoa` | `ten_khoa` | Có |
| `NgayNhap` | `ngay_nhap` | Có |
| `NgayXuat` | `ngay_xuat` | Không |
| `DoiTuong` | `doi_tuong` | Có |
| `TrangThai` | `trang_thai` | Có |
| `MaICD` | `ma_icd` | Không |
| `TenICD` | `ten_icd` | Không |
| `ChanDoan` | `chan_doan` | Không |

---

## 8. Frontend — viết 1 lần, dùng mọi dashboard

```typescript
interface KpiGrid   { type: 'kpi-grid';  id: string; title: string; items: KpiItem[] }
interface PieChart  { type: 'pie-chart'; id: string; title: string; data: PieSlice[] }
interface BarChart  { type: 'bar-chart'; id: string; title: string; data: BarItem[] }
interface DataTable { type: 'table'; id: string; title: string; columns: TableCol[]; rows: Record<string, unknown>[] }
type DashboardSection = KpiGrid | PieChart | BarChart | DataTable
```

```tsx
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

## 9. SSE Push: MatchingWorker → NotificationService → Frontend

Khi `MatchingWorker` xử lý xong một batch records, nó publish `DashboardFeReadyIntegrationEvent` qua MassTransit. `NotificationService` nhận event rồi broadcast SSE xuống tất cả frontend đang kết nối.

### Flow đầy đủ

```
DataMatchingService
  └── MatchingWorker (chạy mỗi 30s)
        │  xử lý xong batch → IEventBus.PublishAsync(DashboardFeReadyIntegrationEvent)
        │  EF Core Outbox lưu message vào DB cùng transaction SaveChangesAsync
        ▼
RabbitMQ
  Exchange: dashboard-fe-ready [fanout]
        │
        ▼
  Queue: notification-dashboard-fe-ready
        │
        ▼
NotificationService
  └── DashboardFeReadyConsumer
        └── DashboardFeReadyHandler
              └── INotificationPusher.BroadcastEventAsync("dashboard-fe-ready", ...)
                    │
                    ▼
              SseConnectionManager → tất cả Channel<string> đang mở
                    │
                    ▼
              Browser (EventSource) nhận event → refresh dashboard
```

### Publisher — DataMatchingService

`MatchingWorker` là **BackgroundService** chạy định kỳ:

```csharp
// DataMatchingService.Infrastructure/Workers/MatchingWorker.cs
private async Task ProcessBatchAsync(CancellationToken ct)
{
    using var scope = scopeFactory.CreateScope();
    var records  = scope.ServiceProvider.GetRequiredService<IStagingRecordRepository>();
    var uow      = scope.ServiceProvider.GetRequiredService<IDataMatchingUnitOfWork>();
    var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

    var batch = await records.GetPendingBatchAsync(50, ct);
    if (batch.Count == 0) return;

    var affectedSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var processed = 0;
    foreach (var record in batch)
    {
        // ... match logic ...
        affectedSystems.Add(record.SourceSystem);
        processed++;
    }

    if (processed > 0)
        await eventBus.PublishAsync(
            new DashboardFeReadyIntegrationEvent(processed, [.. affectedSystems]), ct);

    await uow.SaveChangesAsync(ct);  // commit records đã match + outbox message cùng 1 transaction
}
```

> `IEventBus` phải lấy từ cùng `IServiceScope` với `IDataMatchingUnitOfWork` để outbox hoạt động trong cùng DbContext transaction.

**Contract:**

```csharp
// BuildingBlocks/Contracts/IntegrationEvents/DashboardFeReadyIntegrationEvent.cs
public sealed record DashboardFeReadyIntegrationEvent(
    int      ProcessedCount,
    string[] AffectedSystems)
    : IntegrationEvent;
```

### Consumer và Handler — NotificationService

```csharp
// NotificationService.Infrastructure/Consumers/DashboardFeReadyConsumer.cs
public sealed class DashboardFeReadyConsumer(DashboardFeReadyHandler handler)
    : IConsumer<DashboardFeReadyIntegrationEvent>
{
    public Task Consume(ConsumeContext<DashboardFeReadyIntegrationEvent> context)
        => handler.HandleAsync(context.Message, context.CancellationToken);
}
```

```csharp
// NotificationService.Application/EventHandlers/DashboardFeReadyHandler.cs
public sealed class DashboardFeReadyHandler(INotificationPusher pusher, ILogger<DashboardFeReadyHandler> logger)
    : IIntegrationEventHandler<DashboardFeReadyIntegrationEvent>
{
    public async Task HandleAsync(DashboardFeReadyIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Broadcasting dashboard-fe-ready: {Count} records, systems=[{Systems}]",
            @event.ProcessedCount, string.Join(", ", @event.AffectedSystems));

        await pusher.BroadcastEventAsync(
            "dashboard-fe-ready",
            new { processedCount = @event.ProcessedCount, affectedSystems = @event.AffectedSystems }, ct);
    }
}
```

### SSE event format nhận được ở frontend

```
event: notification
data: {
  "type": "dashboard-fe-ready",
  "payload": { "processedCount": 42, "affectedSystems": ["his-01", "lis-02"] },
  "occurredAtUtc": "2026-05-29T10:00:00Z"
}
```

### JavaScript integration

```javascript
const es = new EventSource(`/notifications/sse?access_token=${token}`);

es.addEventListener('notification', (e) => {
  const msg = JSON.parse(e.data);
  if (msg.type === 'dashboard-fe-ready') {
    const { affectedSystems, processedCount } = msg.payload;
    if (affectedSystems.includes(currentSourceSystem)) {
      fetchDashboard(currentDashboardCode, currentSourceSystem, currentDate);
    }
  }
});
```

### Topology trong RabbitMQ Management

```
Exchanges:
  Hdos.Contracts.IntegrationEvents:DashboardFeReadyIntegrationEvent [fanout]
      └── binding → Exchange: dashboard-fe-ready
  dashboard-fe-ready [fanout]
      └── binding → Queue: notification-dashboard-fe-ready

Queues:
  notification-dashboard-fe-ready   (consumer: DashboardFeReadyConsumer)
```

### Test thủ công SSE

```bash
# 1. Mở SSE stream
curl -N "http://localhost:5000/notifications/sse?access_token=<jwt>"
# Phải nhận ngay: : connected

# 2. Publish message thủ công vào exchange (RabbitMQ Management UI)
# Exchanges → dashboard-fe-ready → Publish message:
{
  "messageType": ["urn:message:Hdos.Contracts.IntegrationEvents:DashboardFeReadyIntegrationEvent"],
  "message": { "processedCount": 5, "affectedSystems": ["his-01"],
               "eventId": "00000000-0000-0000-0000-000000000001",
               "occurredOnUtc": "2026-05-29T10:00:00Z" }
}
```

Terminal `curl` SSE phải nhận:
```
event: notification
data: {"type":"dashboard-fe-ready","payload":{"processedCount":5,"affectedSystems":["his-01"]},"occurredAtUtc":"..."}
```

---

## 10. Checklist thêm dashboard mới

```
[1] POST /dm/sources          — đăng ký SourceProfile + mappings (1 lần)
[2] POST /dm/ingest/file      — upload file JSON array để test
[3] Tạo XxxDashboardConfig.cs — override Code, Title, RecordTypes, BuildSections()
[4] +1 dòng DI                — services.AddSingleton<DashboardConfig, XxxDashboardConfig>()
[5] GET /dm/dashboards/{code} — kiểm tra kết quả
[6] Frontend: EventSource "/notifications/sse" → lắng nghe "dashboard-fe-ready" để auto-refresh
```
