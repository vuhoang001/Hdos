# 51 — Charts & Dashboards: System Overview

> **Mục đích.** Tài liệu **chỉ-mục tổng quan** cho toàn bộ hệ thống chart trong Hdos.
> Bạn cần hiểu high-level architecture, decision matrix, endpoint catalog, file
> location — đọc doc này. Cần implement / debug cụ thể → đi sang docs chi tiết
> (48-50 reference cuối doc).
>
> **Đặc biệt sát thực tế deploy hiện tại** (2026-06-08): liệt kê endpoint **đã wire**,
> chart **đã đăng ký** trên server `192.168.100.60`, và rõ phần nào **chưa wire**
> (vd `/dm/dashboards` mentioned trong doc 25 nhưng chưa có controller).

---

## 0. TL;DR

Hdos cung cấp **2 path** để render chart, cả 2 trả về cùng JSON shape `SduiPage`:

| Path | Endpoint | Nguồn data | Cần ingest? |
|---|---|---|---|
| **A — qua kho trung gian** | `GET /dm/pages/{code}` | StagingRecord (DataMatching DB) | ✅ Có |
| **B — query thẳng lakehouse** | `GET /lakehouse/charts/{code}` | Lakehouse PG (view hoặc raw table) | ❌ Không |

FE chỉ cần **1 renderer** (`<SduiPageView>` ở doc 48) cho cả 2 — vì JSON shape giống.

---

## 1. Đối tượng đọc

| Vai trò | Đọc gì sau doc 51 |
|---|---|
| FE developer | doc 48 (render `SduiPage`) |
| BE developer thêm chart qua Path A | doc 49 (`SduiPageConfig`) |
| BE developer thêm chart qua Path B | doc 50 (`ILakehouseChartConfig` raw SQL) |
| Admin onboard data nguồn mới | doc 46 (playbook) |
| QA verify pipeline | doc 47 (test MVP B) |

---

## 2. Big picture — 2 path

### 2.1 Sơ đồ kiến trúc

```
┌──────────────────────────────────────────────────────────────────────┐
│                      LAKEHOUSE WAREHOUSE (PG)                        │
│  Views: api.bed_occupancy, api.finance_daily, api.clinical_pathway   │
│  Raw tables: raw.invoices, raw.encounters, master.departments, ...   │
└────────────────────────────────────────────────────┬─────────────────┘
                                                     │
            ┌────────────────────────────────────────┼─────────────────┐
            │                                        │                 │
            │ PATH A: SYNC + STAGINGRECORD            │ PATH B: DIRECT │
            │                                        │                 │
            ▼                                        ▼                 │
   ┌──────────────────────┐               ┌──────────────────────┐    │
   │ Lakehouse Service    │               │ Lakehouse Service    │    │
   │  • WarehouseSyncer   │               │  • LakehouseChart    │    │
   │  • ViewBinding       │               │    Builder           │    │
   │  • with-auto-profile │               │  • ChartConfigs/     │    │
   └──────────┬───────────┘               └──────────┬───────────┘    │
              │                                      │                 │
              │ Publish RabbitMQ event               │ Raw SQL via     │
              │ RawRecordIngestRequested             │ NpgsqlCommand   │
              │                                      │ (đọc thẳng PG)  │
              ▼                                      │                 │
   ┌──────────────────────┐                          │                 │
   │ DataMatching Service │                          │                 │
   │  • IngestCoreService │                          │                 │
   │  • SourceProfile     │                          │                 │
   │    mapping           │                          │                 │
   │  • StagingRecord     │                          │                 │
   │    (canonical)       │                          │                 │
   └──────────┬───────────┘                          │                 │
              │                                      │                 │
              │ SduiEngine fetch                     │                 │
              │ + SduiPageConfig                     │                 │
              │ build widgets                        │                 │
              ▼                                      ▼                 │
   ┌──────────────────────┐               ┌──────────────────────┐    │
   │ GET /dm/pages/{code} │               │ GET /lakehouse/      │    │
   │                      │               │      charts/{code}   │    │
   └──────────┬───────────┘               └──────────┬───────────┘    │
              │                                      │                 │
              └──────────────┬───────────────────────┘                 │
                             │                                         │
                             ▼  SduiPage JSON (cùng shape)             │
                  ┌────────────────────────┐                           │
                  │ FE <SduiPageView>      │                           │
                  │ render generic theo    │                           │
                  │ component.type         │                           │
                  └────────────────────────┘                           │
                                                                       │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 Bảng so sánh

| Khía cạnh | Path A `/dm/pages/{code}` | Path B `/lakehouse/charts/{code}` |
|---|---|---|
| **Nguồn data** | StagingRecord (DataMatching DB) | Lakehouse PG trực tiếp |
| **Cần ingest?** | ✅ Có (qua sync hoặc REST push) | ❌ Không — query live |
| **Cần SourceProfile?** | ✅ Có | ❌ Không (vẫn nên cho convention) |
| **SQL ở đâu?** | LINQ in-memory | **Raw SQL Npgsql** trong file C# |
| **Realtime?** | Trễ theo chu kỳ sync | ✅ Live mỗi request |
| **JOIN nhiều bảng** | Phức tạp (LINQ cross-record) | ✅ `JOIN` trong SQL |
| **Aggregate** | LINQ `.GroupBy()` in-memory | ✅ Server-side `GROUP BY` |
| **Share data với DynamicForm DataSource** | ✅ (đọc `/dm/records/{id}`) | ❌ |
| **Filter động** | LINQ `.Where()` | `WHERE` trong SQL |
| **Phù hợp khi** | Cần share data multi-module | Chart độc lập, control SQL hoàn toàn |
| **Touch file khi thêm chart** | 2 file (config + DI) | 2 file (config + DI) |

→ Cả 2 path đều **2 file/chart** — chi phí dev tương đương. Khác biệt ở data architecture.

---

## 3. Endpoint catalog (sống trên server)

### 3.1 Path A — DataMatching

| Method | URL | Mục đích |
|---|---|---|
| `GET` | `/dm/pages` | List page code đã đăng ký |
| `GET` | `/dm/pages/{code}` | Render SDUI page (qua StagingRecord) |
| `GET` | `/dm/records` | Tìm raw record (cho DynamicForm DataSource) |
| `GET` | `/dm/records/{id}` | Lấy 1 record + canonicalPayload |
| `GET` | `/dm/sources` | List SourceProfile |
| `POST` | `/dm/sources` | Đăng ký SourceProfile thủ công |
| `POST` | `/dm/ingest/json` | Push 1 record (HIS pattern) |
| `POST` | `/dm/ingest/file` | Push batch JSON/CSV |

### 3.2 Path B — Lakehouse

| Method | URL | Mục đích |
|---|---|---|
| `GET` | `/lakehouse/charts` | List chart code đã đăng ký |
| `GET` | `/lakehouse/charts/{code}` | Render chart từ raw SQL Npgsql |
| `GET` | `/lakehouse/view-bindings` | List binding |
| `POST` | `/lakehouse/view-bindings/with-auto-profile` | Auto-enroll SourceProfile + tạo binding (MVP B) |
| `POST` | `/lakehouse/view-bindings/{id}/sync` | Trigger sync (chỉ Path A flow) |

### 3.3 Query params chuẩn

Mọi chart endpoint chấp nhận:

| Param | Mô tả | Default |
|---|---|---|
| `date=yyyy-MM-dd` | Ngày báo cáo | Hôm nay UTC |
| `sourceSystem=` (Path A) | Lọc theo source | Tất cả |
| `department=` (Path B finance) | DepartmentId filter | Tất cả |
| `demo=true` (Path B charts hỗ trợ) | Fake data, không đụng DB | `false` |

Mỗi chart config tự document filter nó hỗ trợ ở XML doc trên class.

---

## 4. Decision matrix — khi nào dùng path nào

```
                  ┌───────────────────────────────────┐
                  │ Data có cần share với module      │
                  │ khác (DynamicForm DataSource)?    │
                  └───────────┬───────────────────────┘
                              │
              ┌───────────────┴───────────────┐
              │ Có                            │ Không
              ▼                               ▼
        ┌─────────────┐         ┌─────────────────────────┐
        │  PATH A     │         │ View đã có sẵn trong    │
        │  /dm/pages  │         │ lakehouse?              │
        └─────────────┘         └───────────┬─────────────┘
                                            │
                          ┌─────────────────┴────────────────┐
                          │ Có                                │ Không
                          ▼                                   ▼
                    ┌──────────────┐               ┌────────────────────┐
                    │ Chart fixed, │               │ Cần JOIN nhiều bảng │
                    │ ít thay đổi  │               │ hoặc DE chưa làm   │
                    └──────┬───────┘               │ view?              │
                           │                       └──────┬─────────────┘
                  ┌────────┴────────┐                     │
                  │ Có              │ Không               ▼
                  ▼                 ▼          ┌──────────────────────┐
            ┌──────────┐    ┌──────────────┐   │ PATH B + raw tables  │
            │  PATH A  │    │   PATH B     │   │ /lakehouse/charts/*  │
            │ recommend│    │ (view query) │   │ (JOIN trong SQL)     │
            └──────────┘    └──────────────┘   └──────────────────────┘
```

---

## 5. File structure trong repo

### 5.1 Path A — DataMatching service

```
src/Services/DataMatchingService/
├── DataMatchingService.API/
│   └── Controllers/
│       └── PagesController.cs                 ← /dm/pages/{code}
├── DataMatchingService.Application/
│   ├── Sdui/
│   │   ├── SduiEngine.cs                       ← engine fetch + dispatch
│   │   ├── SduiPage.cs                         ← contract record
│   │   ├── SduiComponent.cs                    ← 5 component types
│   │   ├── SduiPageConfig.cs                   ← ABSTRACT base
│   │   └── Pages/
│   │       ├── ExecutiveSduiConfig.cs          ← page "executive"
│   │       ├── BedOccupancySduiConfig.cs       ← page "bed-occupancy"
│   │       └── FinanceDailySduiConfig.cs       ← page "finance-daily"
│   └── DependencyInjection.cs                  ← register SduiPageConfig
```

### 5.2 Path B — Lakehouse service

```
src/Services/LakehouseService/
├── LakehouseService.API/
│   └── Controllers/
│       └── LakehouseChartsController.cs        ← /lakehouse/charts/{code}
├── LakehouseService.Application/
│   └── Charts/Sdui/
│       ├── SduiPage.cs                         ← DUPLICATE shape (CLAUDE.md §8)
│       └── SduiComponent.cs                    ← DUPLICATE shape
└── LakehouseService.Infrastructure/
    └── Charts/
        ├── ILakehouseChartConfig.cs            ← interface
        ├── LakehouseChartBuilder.cs            ← registry + lookup
        └── Configs/
            ├── BedOccupancyLakehouseChart.cs   ← chart "bed-occupancy"
            └── FinanceDailyLakehouseChart.cs   ← chart "finance-daily" raw tables
```

**Lưu ý:** `SduiPage` / `SduiComponent` records được **duplicate** ở 2 service vì
CLAUDE.md §8 cấm import code cross-service. JSON shape giống → FE không phân biệt.

---

## 6. Quy trình thêm chart mới

### 6.1 Path A — qua StagingRecord (cần data đã ingest)

```
[1] Đảm bảo data đã ingest vào StagingRecord
    • Cách 1: POST /lakehouse/view-bindings/with-auto-profile (auto enroll + sync)
    • Cách 2: POST /dm/sources + POST /dm/ingest/json (push thủ công)

[2] Tạo SduiPageConfig kế thừa abstract base
    src/Services/DataMatchingService/DataMatchingService.Application/Sdui/Pages/

[3] AddSingleton<SduiPageConfig, YourConfig>() trong DI

[4] Build + restart datamatchingservice

[5] FE gọi GET /dm/pages/{your-code}
```

→ Chi tiết: **[doc 49](./49-add-new-sdui-page-config-guide.md)**.

### 6.2 Path B — direct SQL lakehouse (không cần ingest)

```
[1] Discover view/table trong lakehouse PG
    psql -c "\d+ api.your_view"   hoặc  "\dt *.*" cho raw tables

[2] Tạo ILakehouseChartConfig với raw SQL
    src/Services/LakehouseService/LakehouseService.Infrastructure/Charts/Configs/

[3] AddSingleton<ILakehouseChartConfig, YourChart>() trong DI

[4] Build + restart lakehouseservice

[5] FE gọi GET /lakehouse/charts/{your-code}
```

→ Chi tiết: **[doc 50](./50-add-new-lakehouse-chart-guide.md)**.

---

## 7. Khi nào kết hợp cả 2 (hybrid)?

Production tốt nhất là **mix**, không pick exclusive:

```
[Master data / slow-changing]           [Aggregate dashboard realtime]
  Bệnh nhân, danh mục thuốc                Giường, doanh thu, KPI live
  Lịch sử encounter                        Lượt khám hôm nay
       │                                      │
       ▼                                      ▼
   Path A                                  Path B
   /dm/pages/*                            /lakehouse/charts/*
   (qua StagingRecord)                    (query thẳng)
```

FE dùng **cùng 1 renderer** (`<SduiPageView>`) cho cả 2 → không có gánh nặng cognitive.

---

## 8. Reserved / chưa wire

| Thứ | Trạng thái | Lý do |
|---|---|---|
| `GET /dm/dashboards/{code}` | ❌ **Code có nhưng controller chưa wire** | `DashboardEngine` + `DashboardConfig` (M02DashboardConfig) đã đăng ký DI, nhưng `DashboardsController` chưa tạo. Để mở: viết 1 controller ~20 dòng. Xem doc 25 §1 cho concept khác với SDUI. |
| `GET /dm/reports/{code}` | ❌ Tương tự — handler GetReportQuery có, controller không | Tương tự, cần controller |
| Incremental sync `WHERE updated_at > @lastSync` | ❌ Sync hiện full-scan | `updatedAtColumn` đã optional (commit 5a0235c). Khi pipeline upgrade incremental, field này được dùng. |
| Background worker tự sync theo `pollIntervalSeconds` | ❌ Field reserve | Trigger sync hiện qua admin endpoint hoặc `WarehouseRefreshedConsumer` (RabbitMQ event). |
| Widget `EmbedSduiPage` trong DynamicForm | ❌ Chưa làm | Use case A từ session trước — sẽ cho phép thiết kế screen DynamicForm nhúng chart Path B. |

---

## 9. Trạng thái deploy hiện tại (2026-06-08)

### 9.1 Endpoints sống

```bash
BASE=https://192.168.100.60:8443

# Path A
curl -k "$BASE/dm/pages"                  # ["bed-occupancy", "executive", "finance-daily"]
curl -k "$BASE/dm/pages/executive"        # ✅
curl -k "$BASE/dm/pages/bed-occupancy"    # ✅
curl -k "$BASE/dm/pages/finance-daily"    # ✅ — data đã sync từ api.finance_daily view

# Path B
curl -k "$BASE/lakehouse/charts"          # ["bed-occupancy", "finance-daily"]
curl -k "$BASE/lakehouse/charts/bed-occupancy"             # ✅ query api.bed_occupancy view
curl -k "$BASE/lakehouse/charts/bed-occupancy?demo=true"   # ✅ fake data
curl -k "$BASE/lakehouse/charts/finance-daily?demo=true"   # ✅ fake data
curl -k "$BASE/lakehouse/charts/finance-daily"             # ⚠ 500 — raw tables placeholder chưa fill
```

### 9.2 Chart inventory

| Code | Path | Source | Trạng thái |
|---|---|---|---|
| `executive` | A | `benh-nhan-noi-tru` + `cau-hinh-giuong` | ✅ Sống |
| `bed-occupancy` | A | `bed-occupancy` (sync từ `api.bed_occupancy`) | ✅ Sống |
| `finance-daily` | A | `finance-daily` (sync từ `api.finance_daily`) | ✅ Sống |
| `bed-occupancy` | B | View `api.bed_occupancy` (raw SQL) | ✅ Sống |
| `finance-daily` | B | Raw tables `raw.invoices` + JOIN | ⚠ Placeholder — `?demo=true` sống |

### 9.3 SourceProfiles đã enroll (Path A dùng)

```
his-01            / benh-nhan-noi-tru, cau-hinh-giuong, phau-thuat, ...
lakehouse:bed_occupancy        / bed-occupancy
lakehouse:hospital_kpi_summary / hospital-kpi-summary
lakehouse:finance_daily        / finance-daily
lakehouse                      / clinical-pathway
```

→ Curl `GET /dm/sources` để xem real-time.

---

## 10. Quick reference — 1 dòng lệnh tóm tắt

```
Thêm chart Path A:    Sdui/Pages/<Name>.cs + AddSingleton<SduiPageConfig, ...>()
Thêm chart Path B:    Charts/Configs/<Name>.cs + AddSingleton<ILakehouseChartConfig, ...>()
FE render:            <SduiPageView page={fetched.data} />  (doc 48)
Smoke test:           curl /<base>/charts | jq  (list codes)
Demo mode:            ?demo=true (Path B hỗ trợ với bed-occupancy + finance-daily)
```

---

## 11. Related docs

| Doc | Khi nào đọc |
|---|---|
| [25 — SDUI concept](./25-sdui-server-driven-ui.md) | Hiểu khái niệm SDUI ban đầu |
| [38 — FE SDUI Implementation](./38-frontend-sdui-implementation-guide.md) | FE generic renderer (chủ yếu cho DynamicForm `/forms/screens/...`) |
| [44 — Unified Ingest Pipeline](./44-unified-ingest-pipeline.md) | Hiểu vì sao Path A cần ingest |
| [45 — Auto-Enroll SourceProfile](./45-lakehouse-auto-sourceprofile.md) | MVP B với-auto-profile chi tiết |
| [46 — Playbook Add Source Data](./46-playbook-add-source-data.md) | Onboard data step-by-step |
| [47 — Test MVP B Lakehouse View](./47-test-mvp-b-lakehouse-view.md) | QA verify pipeline |
| [48 — FE Consume /dm/pages](./48-frontend-consume-dm-pages-chart-guide.md) | FE renderer chi tiết cho chart |
| [49 — BE Path A SduiPageConfig](./49-add-new-sdui-page-config-guide.md) | Thêm chart Path A |
| [50 — BE Path B LakehouseChart](./50-add-new-lakehouse-chart-guide.md) | Thêm chart Path B + raw SQL |

---

## 12. Changelog

- **2026-06-08** — Initial. System overview cho Path A + Path B chart pipelines, endpoint catalog, decision matrix, file structure, deploy status. Note phần reserved (`/dm/dashboards`, `/dm/reports`, incremental sync) chưa wire.
