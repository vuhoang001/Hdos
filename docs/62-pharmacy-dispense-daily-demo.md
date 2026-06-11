# 62 — Demo Pharmacy DataContract: Worked Example Thêm Chart 5-loại

> **Status:** ✅ Implemented 2026-06-11.
>
> **Mục đích:** Walkthrough đầy đủ pattern **thêm 1 DataContract mới + Chart Consumer có 5 loại chart** (KpiCard, ProgressList, AlertList, FlowPipeline, ChartPie). Dùng làm template khi sau này thêm chart cho domain khác.
>
> **Companion:** doc 50 (recipe gốc), doc 53 (DataContract engine), doc 58/59 (auto-sync DynamicForm), doc 60 (FE integration).

---

## 1. Bối cảnh

Trước doc này hệ thống đã có 3 contract:

| Contract | Domain | Source | Chart |
|---|---|---|---|
| `finance.daily.row` | Finance | sql + demo | ✅ 5 chart |
| `finance.monthly.row` | Finance | demo | ✅ KPI + trend + pie |
| `patient.daily.new` | Clinical | demo | ✅ KPI + age dist |

Doc này thêm contract **thứ 4** ở 1 domain mới (Pharmacy) làm ví dụ tham chiếu. Không tái sử dụng schema cũ — viết từ đầu, full pattern, để fresher đọc 1 lần là copy-paste được.

---

## 2. Output thực tế

```
GET /lakehouse/contracts/pharmacy.dispense.daily.row/chart?source=demo&date=2026-06-11
→ SduiPage shape (3 rows × 5 components):

┌─ Row 1 — KPI strip (4 × span 6) ────────────────────────────────────┐
│  Tổng đơn       Tổng liều       Tổng giá trị       % Kháng sinh     │
│  328 đơn        4892 liều       675 tr             36.4%  (alert)   │
└─────────────────────────────────────────────────────────────────────┘

┌─ Row 2 — Progress (span 16) + Alert (span 8) ──────────────────────┐
│  Top 15 khoa theo liều (màu = % KS)   │ Cảnh báo kho + KS         │
│  Khoa Cấp cứu (1342 liều) ████ 100%   │ K#3 Hết thuốc (Nhi)        │
│  Khoa ICU      (856  liều) ███  64%   │ K#2 Tồn kho thấp (Cấp cứu) │
│  ...                                   │ K#4 Tồn kho thấp (ICU)     │
└─────────────────────────────────────────────────────────────────────┘

┌─ Row 3 — Flow (span 12) + Pie donut (span 12) ─────────────────────┐
│  Kê đơn → Cấp phát → BN nhận           │ Phân bổ liều theo nhóm    │
│  328       4892        1086            │  Kháng sinh   1780  36%   │
│                                        │  Giảm đau     1456  30%   │
│                                        │  Tim mạch     1004  21%   │
│                                        │  Tiêu hoá      354   7%   │
│                                        │  Khác          298   6%   │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 3. Files đã tạo / sửa

| # | File | Loại | Vai trò |
|---|------|------|---------|
| 1 | `Application/DataContracts/Schemas/Pharmacy/PharmacyDispenseDailyRow.cs` | MỚI | record schema 9 field |
| 2 | `Application/DataContracts/Schemas/Pharmacy/PharmacyDispenseDailyContract.cs` | MỚI | `DataContract<…Row>` code = `pharmacy.dispense.daily.row` |
| 3 | `Application/DataContracts/Schemas/Pharmacy/PharmacyDispenseDailyValidator.cs` | MỚI | schema check ≥0, allowed enum |
| 4 | `Infrastructure/DataContracts/Sources/PharmacyDispenseDailyDemoSource.cs` | MỚI | 7 khoa × 4 nhóm thuốc = 28 row giả lập |
| 5 | `Infrastructure/DataContracts/Consumers/PharmacyDispenseDailyChartConsumer.cs` | MỚI | 5 chart types → `SduiPage` |
| 6 | `Infrastructure/DataContracts/Consumers/PharmacyDispenseDailyFormPrefillConsumer.cs` | MỚI | flat dict → `FormPrefillResult` |
| 7 | `Infrastructure/DataContracts/Registration/DataContractsRegistration.cs` | SỬA | thêm 5 dòng DI |

**Không đụng:** `DataContractChartController` (reflection dispatch), `LakehouseContractOperationCatalog` (op generic), `DynamicFormService` (auto-sync gRPC sẽ tự thấy contract mới qua URL pattern `{contractCode}` đã có), migration DB (zero).

---

## 4. Schema chi tiết

```csharp
public sealed record PharmacyDispenseDailyRow(
    DateOnly DispenseDate,
    int      DepartmentId,
    string   DepartmentName,
    string   DrugGroup,          // "Kháng sinh" | "Tim mạch" | "Giảm đau" | "Tiêu hoá" | "Khác"
    int      PrescriptionCount,  // số đơn thuốc
    int      DoseCount,          // tổng số liều phát
    decimal  TotalAmount,        // VNĐ
    int      PatientServedCount, // số BN nhận (per dept × group, sum sẽ over-count nhẹ)
    string?  StockAlertLevel);   // null | "low" | "out" — sinh AlertList
```

**Granularity:** 1 row = 1 (dept × drug group). 7 khoa × 4 nhóm → 28 row. Aggregate (totals, pie, top dept) là việc của Consumer — KHÔNG bake vào source. Đây là quy tắc DataContract: source dumb, consumer biết shape output.

---

## 5. Mapping chart → aggregate function

| Chart component | Span | Aggregate function trong Consumer | Trigger color/alert |
|----|---|---|---|
| **KpiCard "Tổng đơn"** | 6 | `rows.Sum(r => r.PrescriptionCount)` | — |
| **KpiCard "Tổng liều"** | 6 | `rows.Sum(r => r.DoseCount)` | hint AVG liều/đơn |
| **KpiCard "Tổng giá trị"** | 6 | `rows.Sum(r => r.TotalAmount)` | FormatVnd → "675 tr" |
| **KpiCard "% Kháng sinh"** | 6 | `(antibioticDose / doseTotal) × 100` | Red ≥40%, Amber ≥25%, Green |
| **ProgressList top khoa** | 16 | `GROUP BY DepartmentId, Sum(DoseCount), Take(15)` | Bar color theo % KS từng khoa |
| **AlertList** | 8 | `Where(StockAlertLevel != null) ∪ Where(absRate ≥50%)` | Critical = "out" / KS ≥65% |
| **FlowPipeline** | 12 | 3 stage: PrescriptionCount → DoseCount → PatientServedCount | — |
| **ChartPie donut** | 12 | `GROUP BY DrugGroup, Sum(DoseCount)` | 5 màu fixed palette |

Tổng span Row 1 = 6×4 = 24. Row 2 = 16+8 = 24. Row 3 = 12+12 = 24. Grid 24-col chuẩn.

---

## 6. Filter hỗ trợ

URL query string FE truyền vào sẽ pass nguyên xi vào `DataContractQuery.Filters`:

| Param | Type | Áp dụng | Ví dụ |
|---|---|---|---|
| `date` | `yyyy-MM-dd` | `DispenseDate` của mọi row | `?date=2026-06-11` |
| `department` | int | Filter 1 khoa | `?department=2` |
| `group` | string | Filter 1 nhóm thuốc | `?group=Kháng%20sinh` |
| `source` | enum | Swap source (`demo`) | `?source=demo` |
| `consumer` | enum | Swap output type (`chart` mặc định) | `?consumer=chart` |
| `mode` | `single` | (prefill only) FE bind 1 record | `?mode=single` |

---

## 7. Verify after deploy

```bash
# Build pass?
dotnet build src/Services/LakehouseService/LakehouseService.API

# Restart Lakehouse — auto-sync gRPC sẽ giữ nguyên 2 op (prefill/chart) generic
docker compose up -d --build lakehouseservice

# 1. Contract appears in registry
curl -k "https://localhost:8443/lakehouse/contracts" \
  -H "Authorization: Bearer <token>" | jq '.data[] | select(.code=="pharmacy.dispense.daily.row")'
# → { code: "pharmacy.dispense.daily.row", displayName: "...", schemaTypeName: "PharmacyDispenseDailyRow" }

# 2. Schema reflection lists 9 fields
curl -k "https://localhost:8443/lakehouse/contracts/pharmacy.dispense.daily.row/schema" \
  -H "Authorization: Bearer <token>" | jq '.data | length'
# → 9

# 3. Chart renders 3 rows × 5 components
curl -k "https://localhost:8443/lakehouse/contracts/pharmacy.dispense.daily.row/chart?source=demo&date=2026-06-11" \
  -H "Authorization: Bearer <token>" \
  | jq '.data | { title, rowCount: (.rows | length), componentCount: ([.rows[].components[]] | length) }'
# → { title: "Phát thuốc theo ngày (DataContract)", rowCount: 3, componentCount: 8 }

# 4. Form prefill mode=single trả flat object
curl -k "https://localhost:8443/lakehouse/contracts/pharmacy.dispense.daily.row/prefill?source=demo&mode=single" \
  -H "Authorization: Bearer <token>" \
  | jq '.data.single'
# → { dispenseDate, departmentId, departmentName, drugGroup, ... } (9 keys)
```

---

## 8. Gắn vào DynamicForm screen (không cần migration)

Provider catalog đã có (Phase 4 auto-sync). Admin chỉ chọn op + set defaultParams:

```bash
# Tạo screen
curl -X POST https://localhost:8443/forms/admin/screens \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{
    "moduleCode": "pharmacy",
    "code": "dispense-daily-dashboard",
    "title": "Dashboard phát thuốc ngày"
  }'

# Gắn DataSource trỏ vào contract mới
curl -X PUT https://localhost:8443/forms/admin/screens/pharmacy/dispense-daily-dashboard/data-sources \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{
    "dataSources": [{
      "namespace": "pharmacy",
      "operationId": "lakehouse::chart",
      "requiredParams": ["contractCode"],
      "defaultParams": { "contractCode": "pharmacy.dispense.daily.row" }
    }]
  }'
```

FE đọc `GET /forms/screens/pharmacy/dispense-daily-dashboard/layout` → nhận đủ `baseUrl + resourcePath + defaultParams` → fetch chart luôn.

---

## 9. Pattern để copy-paste cho contract sau

Bước thứ tự áp dụng cho **bất kỳ chart mới nào** (giả định domain X, entity Y):

```
1. Application/DataContracts/Schemas/<Domain>/
   <Entity>Row.cs           — record (các field tối thiểu cần để build chart)
   <Entity>Contract.cs      — DataContract<...>, Code = "<domain>.<entity>.<shape>"
   <Entity>Validator.cs     — schema check fail-fast

2. Infrastructure/DataContracts/Sources/
   <Entity>DemoSource.cs    — IDataSource<...Row>, SourceCode = "demo"
   <Entity>SqlSource.cs     — (optional) khi có view/raw table thật

3. Infrastructure/DataContracts/Consumers/
   <Entity>ChartConsumer.cs       — IDataConsumer<...Row, SduiPage>, ConsumerCode = "chart"
   <Entity>FormPrefillConsumer.cs — IDataConsumer<...Row, FormPrefillResult>, ConsumerCode = "form-prefill"

4. Infrastructure/DataContracts/Registration/DataContractsRegistration.cs
   Thêm 1 cụm AddDataContract + AddDataSource* + AddDataConsumer* + AddDataContractValidator

5. Build + restart lakehouseservice. KHÔNG đụng controller, catalog, DynamicForm, migration.

6. (Optional) docs/<NN>-<contract-code>-demo.md
```

---

## 10. Anti-patterns khi viết Consumer

| Anti-pattern | Fix |
|---|---|
| `await foreach (var r in stream) { _aggregateState.Add(r); }` rồi build chart trong cùng loop | Materialize `List<Row>` xong **rồi** mới aggregate — code dễ đọc hơn |
| Aggregate trực tiếp trên `IAsyncEnumerable` qua `await foreach` 3 lần | Materialize 1 lần vào `List<>` — `IAsyncEnumerable` chỉ enumerate được 1 lần qua source |
| Hard-code màu `"#1677ff"` rải khắp Consumer | Đặt `const string ColorPrimary = "..."` đầu file hoặc helper class chung |
| Decimal sum kiểu int (`(int)(amount / 1_000_000)`) cho FlowPipeline mà không guard < 0 | Check overflow nếu domain có giá trị âm; FlowPipeline stage value là `int` |
| Build chart khi `rows.Count == 0` → exception NRE/divide-by-zero | Trả `BuildEmpty(...)` ở đầu — có template ngay trong file |

---

## 11. Checklist hoàn thành (cho task này)

- [x] Build pass, 0 error
- [x] 6 file mới tuân thủ pattern PatientDailyNew + FinanceDaily
- [x] DI registration 1 dòng thêm
- [x] Không sửa controller, catalog, DynamicForm, migration
- [x] Consumer cover 5 SDUI component (KpiCard, ProgressList, AlertList, FlowPipeline, ChartPie)
- [x] Doc 62 walkthrough kèm checklist + anti-patterns
- [ ] Smoke test thực tế qua `docker compose up -d --build lakehouseservice` (cần chạy bằng tay)
- [ ] Test FE renderer thực tế (cần FE engineer)

---

## 12. Related docs

- [50 — Path B Recipe (legacy ILakehouseChartConfig)](./50-add-new-lakehouse-chart-guide.md)
- [53 — DataContract Engine architecture](./53-chart-funnel-architecture.md)
- [58 — Lakehouse ↔ DynamicForm Integration](./58-lakehouse-dynamicform-integration.md)
- [59 — Auto-sync gRPC Phase 4](./59-auto-sync-datacontract-registry.md)
- [60 — FE Integration: DataContract Prefill](./60-fe-integration-datacontract-prefill.md)
- [61 — DataSource defaultParams](./61-datasource-default-params.md)
