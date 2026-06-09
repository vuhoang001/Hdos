# 53 — Data Contract Engine: "Phễu" Universal cho Chart, Form & Ingest

> **Trạng thái:** Design + Pilot implementation (2026-06-09). Migration đang progressive — endpoint cũ vẫn chạy 100%.
> **Liên quan:** doc 44 (unified ingest), doc 48 (FE consume chart), doc 49 (Path A), doc 50 (Path B), doc 51 (system overview), doc 52 (embed chart vào form).

---

## 1. Bối cảnh

Hệ thống Hdos đã có **3 pattern rời rạc** cho việc chuyển data → UI:

| Pattern | Layer | Registry | Fetch logic | Schema | Khi nào dùng |
|---|---|---|---|---|---|
| `SourceProfile` + `IngestCoreService` | DataMatching | `ISourceProfileRepository` (DB row) | Caller push raw JSON | `FieldMappingsJson` string | Ingest data từ HIS/BHYT/lakehouse view → StagingRecord canonical |
| `SduiPageConfig` + `SduiEngine` | DataMatching | DI singleton list | `IStagingRecordRepository.GetMatchedAsync` | Tự decode JSON trong `BuildPage` | Path A — chart đọc canonical đã ingest |
| `ILakehouseChartConfig` + `LakehouseChartBuilder` | Lakehouse | DI singleton list | `NpgsqlCommand` raw SQL trong từng config | Tuple ad-hoc trong từng config | Path B — chart query thẳng raw tables |

**Vấn đề:**
1. Mỗi pattern là 1 silo — chart Path A không reuse được logic Path B, form pre-fill (DynamicForm `DataSource`) đi đường thứ 4 (HTTP gọi service ngoài).
2. Source coupled với consumer: muốn đổi chart từ "view" sang "code build", phải viết lại config + đăng ký DI khác.
3. Schema implicit (decode JSON ngay trong BuildPage / SQL inline) → không có type safety, không có validator chung.
4. Không có chỗ chung để form, chart, export, notification cùng đọc 1 logical data.

**Mục tiêu của doc này:** xây 1 lớp abstraction "Data Contract" thống nhất — schema khai báo bằng C# record, source có thể là view/SQL/code/API/event tùy ý, consumer (chart/form/export/notification) cùng đọc qua 1 gateway.

---

## 2. Kiến trúc 3 tầng

```
┌──────────────────────────────────────────────────────────────────────┐
│ TẦNG 1 — DATA CONTRACT (BuildingBlocks/Contracts/DataContracts/)     │
│                                                                      │
│   record FinanceDailyRow { ... }                                     │
│   class  FinanceDailyContract : DataContract<FinanceDailyRow>        │
│   class  FinanceDailyValidator : IDataContractValidator<...>         │
│                                                                      │
│   ⇣ là cái gì: SCHEMA + CODE duy nhất + DisplayName + Validator      │
│   ⇣ ở đâu:    chỉ phụ thuộc Microsoft.Extensions.DI.Abstractions     │
└──────────────────────────────────────────────────────────────────────┘
                ▲                              ▲
                │                              │
        ┌───────┴────────┐             ┌───────┴────────┐
        │ TẦNG 2 — SOURCE│             │ TẦNG 2 - CONS. │
        │ (1+ per contr.)│             │ (1+ per contr.)│
        │ • ViewSource   │             │ • ChartConsumer│
        │ • SqlSource    │             │ • FormPrefill  │
        │ • CodeSource   │             │ • ExportCsv    │
        │ • ApiSource    │             │ • NotifyConsumer
        │ • EventSource  │             │                │
        │   (RMQ)        │             │                │
        └───────┬────────┘             └────────┬───────┘
                │                                │
                └────────────┬───────────────────┘
                             ▼
                ┌────────────────────────────┐
                │  DataContractGateway       │  ◀── "phễu"
                │  + DataContractRegistry    │
                │                            │
                │  ReadAsync<T>(...)         │
                │  ValidateAsync<T>(...)     │
                │  ConsumeAsync<T,Out>(...)  │
                └────────────────────────────┘
                             ▲
                             │
                ┌────────────┴───────────┐
                │  Controller / Worker / │
                │  EventHandler caller   │
                └────────────────────────┘
```

### Trách nhiệm

| Tầng | File / Class | Trách nhiệm | Không làm |
|---|---|---|---|
| **Contract** | `IDataContract`, `DataContract<T>`, `IDataContractValidator<T>` | Định nghĩa SCHEMA (record) + CODE + validator | Không biết source, không biết consumer |
| **Source** | `IDataSource<T>` impl | Đọc data từ 1 nguồn cụ thể, trả `IAsyncEnumerable<T>` | Không build chart, không format output |
| **Consumer** | `IDataConsumer<T, TOut>` impl | Biến stream → output cụ thể (chart, csv, json...) | Không biết source là gì, chỉ stream in |
| **Gateway** | `DataContractGateway` | Route caller → source / consumer / validator phù hợp | Không có business logic |
| **Registry** | `DataContractRegistry` | Lookup contract by code, validate uniqueness | Không resolve source/consumer (scoped) |

---

## 3. Interface signatures (đã implement trong codebase)

Tất cả nằm trong `src/BuildingBlocks/Contracts/DataContracts/`:

```csharp
// 1. Contract — khai báo schema
public interface IDataContract
{
    string Code { get; }            // "finance.daily.row"
    Type   SchemaType { get; }      // typeof(FinanceDailyRow)
    string DisplayName { get; }
}

public abstract class DataContract<TSchema> : IDataContract where TSchema : class { ... }

// 2. Source — đọc rows từ 1 nguồn
public interface IDataSource<TSchema> where TSchema : class
{
    string ContractCode { get; }
    string SourceCode { get; }      // "view", "sql", "demo", "rmq", "code"
    IAsyncEnumerable<TSchema> ReadAsync(DataContractQuery query, CancellationToken ct);
}

// 3. Consumer — biến stream → output
public interface IDataConsumer<TSchema, TOutput> where TSchema : class
{
    string ContractCode { get; }
    string ConsumerCode { get; }    // "chart", "form-prefill", "csv-export"
    Task<TOutput> ConsumeAsync(IAsyncEnumerable<TSchema> stream, DataContractQuery query, CancellationToken ct);
}

// 4. Validator — schema-level check
public interface IDataContractValidator<TSchema> where TSchema : class
{
    string ContractCode { get; }
    ValueTask<DataContractValidationResult> ValidateAsync(TSchema row, CancellationToken ct);
}

// 5. Query — string-based filter shape (date, dept, ...)
public sealed record DataContractQuery(IReadOnlyDictionary<string, string?> Filters, int? Limit = null);

// 6. Gateway — phễu trung tâm
public sealed class DataContractGateway
{
    IAsyncEnumerable<TSchema> ReadAsync<TSchema>(string contractCode, string? sourceCode, DataContractQuery q, CancellationToken ct);
    Task<DataContractValidationResult> ValidateAsync<TSchema>(string contractCode, TSchema row, CancellationToken ct);
    Task<TOutput> ConsumeAsync<TSchema, TOutput>(string contractCode, string consumerCode, string? sourceCode, DataContractQuery q, CancellationToken ct);
    IReadOnlyList<IDataSource<TSchema>>   ListSources<TSchema>(string contractCode);
    IReadOnlyList<IDataConsumer<TSchema, TOutput>> ListConsumers<TSchema, TOutput>(string contractCode);
}
```

**Lifetime DI:**
- `IDataContract` — Singleton (metadata)
- `DataContractRegistry` — Singleton (lookup table)
- `DataContractGateway` — **Scoped** (vì resolve `IDataSource<T>` từ request scope)
- `IDataSource<T>`, `IDataConsumer<T, TOut>`, `IDataContractValidator<T>` — Scoped (mặc định — đa số dùng DbContext/HttpClient)

---

## 4. POC End-to-End — FinanceDaily

### 4.1 Schema (Contract layer)

```csharp
// src/BuildingBlocks/Contracts/DataContracts/Finance/FinanceDailyRow.cs
namespace Hdos.Contracts.DataContracts.Finance;

/// 1 row tài chính theo ngày × khoa. Canonical schema dùng chung cho mọi source.
public sealed record FinanceDailyRow(
    DateOnly InvoiceDate,
    int      DepartmentId,
    string   DepartmentName,
    decimal  TotalInvoiceAmount,
    decimal  TotalDiscountAmount,
    int      InvoiceCount,
    int      DistinctEncounterCount,
    string?  FinanceBucket);

// src/BuildingBlocks/Contracts/DataContracts/Finance/FinanceDailyContract.cs
public sealed class FinanceDailyContract : DataContract<FinanceDailyRow>
{
    public const string ContractCode = "finance.daily.row";
    public override string Code => ContractCode;
    public override string DisplayName => "Tài chính theo ngày × khoa (row-level)";
}
```

### 4.2 Source A — Wrap Path A (view DB qua StagingRecord)

```csharp
// src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/Sources/
// FinanceDailyStagingSource.cs
public sealed class FinanceDailyStagingSource : IDataSource<FinanceDailyRow>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public string SourceCode   => "staging";

    private readonly IStagingRecordRepository _staging;

    public FinanceDailyStagingSource(IStagingRecordRepository staging) { _staging = staging; }

    public async IAsyncEnumerable<FinanceDailyRow> ReadAsync(
        DataContractQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        var date = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var raw  = await _staging.GetMatchedAsync(
                       sourceSystem: "lakehouse:finance_daily",
                       recordType:   "finance-daily",
                       fromDate: null, toDate: null, ct);
        foreach (var r in raw)
        {
            using var doc = JsonDocument.Parse(r.CanonicalPayload!);
            yield return MapRow(doc.RootElement, date);
        }
    }

    private static FinanceDailyRow MapRow(JsonElement el, DateOnly fallbackDate) =>
        new(
            InvoiceDate:            el.TryGetProperty("InvoiceDate", out var d) && DateOnly.TryParse(d.GetString(), out var dd) ? dd : fallbackDate,
            DepartmentId:           el.TryGetProperty("DepartmentId", out var x) ? x.GetInt32() : 0,
            DepartmentName:         el.TryGetProperty("DepartmentName", out var n) ? n.GetString() ?? "" : "",
            TotalInvoiceAmount:     el.TryGetProperty("TotalInvoiceAmount", out var t) ? t.GetDecimal() : 0,
            TotalDiscountAmount:    el.TryGetProperty("TotalDiscountAmount", out var dis) ? dis.GetDecimal() : 0,
            InvoiceCount:           el.TryGetProperty("InvoiceCount", out var ic) ? ic.GetInt32() : 0,
            DistinctEncounterCount: el.TryGetProperty("DistinctEncounterCount", out var ec) ? ec.GetInt32() : 0,
            FinanceBucket:          el.TryGetProperty("FinanceBucket", out var b) ? b.GetString() : null);
}
```

### 4.3 Source B — Wrap Path B (raw SQL Lakehouse)

```csharp
// FinanceDailySqlSource.cs
public sealed class FinanceDailySqlSource : IDataSource<FinanceDailyRow>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public string SourceCode   => "sql";

    private readonly NpgsqlDataSource _ds;
    public FinanceDailySqlSource(NpgsqlDataSource ds) { _ds = ds; }

    public async IAsyncEnumerable<FinanceDailyRow> ReadAsync(
        DataContractQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        var date = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var dept = query.GetInt("department");

        const string sql = """
            SELECT
                i.invoice_date,
                i.department_id,
                COALESCE(d.department_name, 'Khoa #' || i.department_id) AS department_name,
                COALESCE(SUM(i.total_amount), 0)::numeric    AS total_invoice_amount,
                COALESCE(SUM(i.discount_amount), 0)::numeric AS total_discount_amount,
                COUNT(i.id)::int                              AS invoice_count,
                COUNT(DISTINCT e.encounter_id)::int          AS distinct_encounter_count,
                i.invoice_type                                AS finance_bucket
            FROM raw.invoices i
            LEFT JOIN master.departments d ON d.id = i.department_id
            LEFT JOIN raw.encounters e ON e.department_id = i.department_id AND e.encounter_date = i.invoice_date
            WHERE i.invoice_date = @d AND (@dept IS NULL OR i.department_id = @dept)
            GROUP BY i.invoice_date, i.department_id, d.department_name, i.invoice_type
            ORDER BY total_invoice_amount DESC NULLS LAST
        """;

        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@d",    date);
        cmd.Parameters.AddWithValue("@dept", (object?)dept ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            yield return new FinanceDailyRow(
                InvoiceDate:            reader.GetFieldValue<DateOnly>(0),
                DepartmentId:           reader.GetInt32(1),
                DepartmentName:         reader.GetString(2),
                TotalInvoiceAmount:     reader.GetDecimal(3),
                TotalDiscountAmount:    reader.GetDecimal(4),
                InvoiceCount:           reader.GetInt32(5),
                DistinctEncounterCount: reader.GetInt32(6),
                FinanceBucket:          reader.IsDBNull(7) ? null : reader.GetString(7));
    }
}
```

### 4.4 Source C — Code-generated (demo, no DB)

```csharp
// FinanceDailyDemoSource.cs
public sealed class FinanceDailyDemoSource : IDataSource<FinanceDailyRow>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public string SourceCode   => "demo";

    public async IAsyncEnumerable<FinanceDailyRow> ReadAsync(
        DataContractQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        var date = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        yield return new(date, 1, "Khoa Tim mạch",       1_240_000_000m, 180_000_000m, 156, 156, "BHYT");
        yield return new(date, 2, "Khoa Hồi sức ICU",      980_000_000m, 320_000_000m,  98,  98, "BHYT");
        yield return new(date, 3, "Khoa Nhi",              720_000_000m,  85_000_000m, 142, 142, "Dịch vụ");
        // ...
    }
}
```

→ **Cùng schema, 3 source khác nhau, chart không cần biết phải gọi cái nào.**

### 4.5 Consumer — Chart (build SduiPage)

```csharp
public sealed class FinanceDailyChartConsumer : IDataConsumer<FinanceDailyRow, SduiPage>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public string ConsumerCode => "chart";

    public async Task<SduiPage> ConsumeAsync(
        IAsyncEnumerable<FinanceDailyRow> stream,
        DataContractQuery query,
        CancellationToken ct)
    {
        var rows = new List<FinanceDailyRow>();
        await foreach (var r in stream.WithCancellation(ct)) rows.Add(r);

        var date     = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var totals   = AggregateTotals(rows);
        var perDept  = rows.GroupBy(r => r.DepartmentId).Select(/*...*/).ToList();
        var perBucket= rows.Where(r => r.FinanceBucket != null)
                           .GroupBy(r => r.FinanceBucket!).Select(/*...*/).ToList();

        return new SduiPage(
            Code:     "finance-daily",
            Title:    "Tài chính theo ngày",
            Badge:    "DataContract",
            Live:     true,
            Subtitle: $"Ngày {date:dd/MM/yyyy} · qua DataContract gateway",
            Actions:  [new("Xuất Excel", "default", null)],
            Rows:     [/* BuildKpiRow, BuildProgressAndAlertRow, BuildFlowAndPieRow */],
            GeneratedAt: DateTime.UtcNow);
    }
    // ... agg helpers
}
```

### 4.6 Consumer khác — CSV Export (cùng contract!)

```csharp
public sealed class FinanceDailyCsvConsumer : IDataConsumer<FinanceDailyRow, byte[]>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public string ConsumerCode => "csv";

    public async Task<byte[]> ConsumeAsync(IAsyncEnumerable<FinanceDailyRow> stream, /*...*/)
    {
        using var ms = new MemoryStream();
        using var sw = new StreamWriter(ms, Encoding.UTF8);
        await sw.WriteLineAsync("Date,DeptId,DeptName,TotalAmount,Discount,InvoiceCount,EncounterCount,Bucket");
        await foreach (var r in stream.WithCancellation(ct))
            await sw.WriteLineAsync($"{r.InvoiceDate},{r.DepartmentId},{r.DepartmentName},{r.TotalInvoiceAmount},{r.TotalDiscountAmount},{r.InvoiceCount},{r.DistinctEncounterCount},{r.FinanceBucket}");
        await sw.FlushAsync();
        return ms.ToArray();
    }
}
```

→ **Cùng 1 contract, 2 consumer. Chart + CSV reuse 100% data source.**

### 4.7 DI Registration

```csharp
// LakehouseService.API/Program.cs hoặc DataContractsRegistration.cs
services
    .AddDataContracts()
    .AddDataContract<FinanceDailyContract>()
    .AddDataSource<FinanceDailyRow, FinanceDailyStagingSource>()
    .AddDataSource<FinanceDailyRow, FinanceDailySqlSource>()
    .AddDataSource<FinanceDailyRow, FinanceDailyDemoSource>()
    .AddDataConsumer<FinanceDailyRow, SduiPage, FinanceDailyChartConsumer>()
    .AddDataConsumer<FinanceDailyRow, byte[],   FinanceDailyCsvConsumer>()
    .AddDataContractValidator<FinanceDailyRow,  FinanceDailyValidator>();
```

### 4.8 Endpoint mới qua Gateway

```csharp
[ApiController]
[Route("lakehouse/contracts")]
public sealed class DataContractChartController : ControllerBase
{
    private readonly DataContractGateway _gateway;
    public DataContractChartController(DataContractGateway gateway) { _gateway = gateway; }

    [HttpGet("{code}/chart")]
    public async Task<IActionResult> GetChart(
        string code,
        [FromQuery(Name = "source")] string? sourceCode,
        [FromQuery(Name = "consumer")] string consumerCode = "chart",
        CancellationToken ct = default)
    {
        var contract = _gateway.Require(code);
        var query    = DataContractQuery.From(
            Request.Query.SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string?>(kv.Key, v))));

        // Dispatch theo SchemaType — boxing chỉ ở root, consumer trả về object generic
        if (contract.SchemaType == typeof(FinanceDailyRow))
        {
            var page = await _gateway.ConsumeAsync<FinanceDailyRow, SduiPage>(code, consumerCode, sourceCode, query, ct);
            return Ok(page);
        }
        return NotFound(new { error = $"Unsupported schema {contract.SchemaType.Name} on this endpoint" });
    }
}
```

→ **Same URL `/lakehouse/contracts/finance.daily.row/chart?source=sql&date=2026-06-09` chạy Path B; thay `source=staging` → Path A; thay `source=demo` → fake data, không đụng DB.**

---

## 5. Migration plan (FinanceDaily làm pilot)

| Phase | Việc | Risk | Backward compat |
|---|---|---|---|
| **P1** | Contract layer trong BuildingBlocks (interfaces + Gateway + Registry) | Zero | Không ai dùng, chỉ thêm |
| **P2** | FinanceDailyRow contract + 3 source + 2 consumer + DI register | Zero | Chart cũ vẫn nguyên |
| **P3** | Endpoint `/lakehouse/contracts/{code}/chart` mới + feature flag `DataContracts__EnableNewEndpoint` | Thấp | Flag off mặc định; FE old vẫn dùng `/lakehouse/charts/finance-daily` |
| **P4** | DynamicForm: `DataContractFormBindingResolver` resolve MANAGED DataSource qua gateway | Trung bình | Resolver fallback HTTP nếu không tìm thấy contract |
| **P5** | DataMatching: `IngestSource<T>` wrap `IngestCoreService` — thêm method `IngestThroughContractAsync<T>` | Trung bình | Method cũ `TryBuildRecordAsync` vẫn còn |
| **P6** | `[Obsolete]` markers cho `SduiPageConfig`, `ILakehouseChartConfig` với hint migrate sang DataContract | Thấp | Soft deprecate — code cũ vẫn build, chỉ có warning |
| **P7** (session khác) | Hard delete `SduiPageConfig` + registry cũ sau khi FE migrate xong | Cao | Yêu cầu canary + rollback plan |

**Quy tắc khi migrate 1 chart hiện có sang DataContract:**
1. Định nghĩa `XxxRow` record + `XxxContract` class trong `Contracts/DataContracts/{Domain}/`
2. Wrap source hiện tại (staging hoặc raw SQL) thành 1 `IDataSource<XxxRow>` impl — copy nguyên logic fetch
3. Tạo `XxxChartConsumer` — copy nguyên logic `BuildPage` từ config cũ, đổi input từ `Dictionary<string, JsonElement>` / `NpgsqlReader` sang `IAsyncEnumerable<XxxRow>`
4. Đăng ký DI
5. Endpoint mới đi qua Gateway. Endpoint cũ KHÔNG xóa.
6. FE test endpoint mới, nếu OK → switch URL trong widget config; nếu fail → rollback bằng cách trỏ về URL cũ.

---

## 6. Convention & quy tắc

### 6.1 Vị trí file

```
src/BuildingBlocks/Contracts/DataContracts/
├── IDataContract.cs                 ← core interfaces
├── IDataSource.cs
├── IDataConsumer.cs
├── IDataContractValidator.cs
├── DataContractQuery.cs
├── DataContractRegistry.cs
├── DataContractGateway.cs
├── DataContractException.cs
├── Extensions/
│   └── DataContractServiceCollectionExtensions.cs
└── {Domain}/                        ← contract per business domain
    ├── Finance/
    │   ├── FinanceDailyRow.cs
    │   └── FinanceDailyContract.cs
    ├── Clinical/
    │   ├── BedOccupancyRow.cs
    │   └── BedOccupancyContract.cs
    └── ...

src/Services/{Service}/{Service}.Infrastructure/DataContracts/
├── Sources/
│   ├── {Domain}{Source}Source.cs    ← e.g. FinanceDailyViewSource, BedOccupancySqlSource
│   └── ...
├── Consumers/
│   ├── {Domain}{Consumer}Consumer.cs ← e.g. FinanceDailyChartConsumer
│   └── ...
└── Registration/
    └── DataContractsRegistration.cs ← extension method AddDataContracts...()
```

### 6.2 Naming

| Loại | Pattern | Ví dụ |
|---|---|---|
| Schema record | `{Domain}Row` (1 row) hoặc `{Domain}Snapshot` (1 thời điểm) | `FinanceDailyRow`, `BedOccupancySnapshot` |
| Contract class | `{Domain}Contract : DataContract<{Domain}Row>` | `FinanceDailyContract` |
| Contract Code | `"{domain}.{entity}.{shape}"` lowercase dot-separated | `"finance.daily.row"`, `"clinical.bed.occupancy.snapshot"` |
| Source class | `{Domain}{SourceKind}Source` | `FinanceDailyViewSource`, `FinanceDailySqlSource`, `FinanceDailyDemoSource`, `FinanceDailyRmqSource` |
| Source Code | lowercase 1 word | `"view"`, `"sql"`, `"demo"`, `"rmq"`, `"api"` |
| Consumer class | `{Domain}{ConsumerKind}Consumer` | `FinanceDailyChartConsumer`, `FinanceDailyCsvConsumer` |
| Consumer Code | lowercase kebab-case | `"chart"`, `"csv"`, `"form-prefill"`, `"notify"` |

### 6.3 Schema versioning (open question — chưa quyết)

Nếu schema cần đổi (thêm field, đổi type), 2 cách:
- **A. Additive evolution**: thêm optional field, không xóa field cũ → mọi source/consumer cũ vẫn chạy. Recommend khi đổi nhỏ.
- **B. New contract code**: `finance.daily.row` → `finance.daily.row.v2` → migrate consumer lần lượt. Recommend khi đổi semantic.

Tránh tự ý đổi type của field hiện có — đó là breaking change cho mọi consumer.

### 6.4 Validation

- Chỉ validate ở RANH GIỚI (khi data đi vào: ingest, form submit, API write). Source đọc từ DB internal KHÔNG cần validate.
- Validator nên fail-fast (return Invalid với message rõ), KHÔNG throw.
- Caller quyết định xử lý lỗi: throw `DataContractValidationException` hay log + skip.

### 6.5 Khi nào KHÔNG nên dùng DataContract

- Data chỉ dùng 1 chỗ duy nhất, không có nhu cầu reuse → giữ raw SQL trực tiếp, đừng over-engineer.
- Read transactional 1-1 với DbContext (CRUD form bình thường) → MediatR + repository như hiện tại.
- Data shape thay đổi mỗi request (dynamic dashboard query builder) → schema-typed contract không hợp; dùng `Dictionary<string, object>` hoặc DataMatching pattern.

---

## 7. Tradeoffs

### ✅ Ưu điểm

- **Decoupling**: chart/form/export không biết source là gì → đổi source 1 dòng DI.
- **Type-safe**: schema là C# record, refactor an toàn, compiler check field name.
- **Reuse**: cùng 1 data flow vào nhiều UI surfaces (chart, form pre-fill, CSV export, notification).
- **Test dễ**: mock `IDataSource<T>` trả fake stream, consumer test độc lập.
- **Progressive migration**: P3 endpoint mới chạy song song endpoint cũ, FE chuyển dần.
- **Discoverable**: `Gateway.AvailableContracts` + `ListSources` + `ListConsumers` cho admin UI inspect.

### ⚠️ Nhược điểm / Risk

- **Abstraction overhead**: thêm 3-4 lớp class cho mỗi chart mới so với code raw SQL inline. Mitigation: template recipe trong doc 50 vẫn dùng được cho POC nhanh, sau migrate.
- **Schema lock-in**: 1 khi `finance.daily.row` có 5 consumer, đổi schema → đụng 5 chỗ. Mitigation: convention §6.3.
- **Schema mismatch runtime exception**: nếu Gateway gọi với TSchema sai → `DataContractSchemaMismatchException` ở runtime, không phải compile-time. Mitigation: integration test cho mỗi contract.
- **Discoverability**: cần 1 endpoint `/lakehouse/contracts` list tất cả contract + source + consumer để dev/admin biết có gì. **TODO P3.**

---

## 8. Open questions cho team

1. **Schema versioning** — additive vs `.v2` suffix? §6.3 — cần thống nhất trước khi có >5 contract.
2. **Validation strictness** — Gateway có nên auto-validate sau mỗi source.Read không, hay để caller quyết? Hiện tại optional, caller gọi `ValidateAsync` riêng nếu muốn.
3. **Cross-service contract** — nếu DataMatching publish event `OrderCreated`, OrderService có nên expose contract `order.created.row` để Notification service consume? Nếu có, contract định nghĩa ở `Contracts` (BuildingBlocks) là đúng. Cần playbook riêng cho event-driven source.
4. **Caching layer** — Gateway có nên cache source output trong process? Nếu chart fetch 100 row mỗi request và stream được cache key theo query → giảm tải DB. Nhưng phải invalidate khi data update. **Đề xuất: NO caching ở layer này, để source tự quyết.**
5. **Metrics** — Gateway emit Prometheus metric `data_contract_read_total{contract,source}` + duration histogram? Cho observability. **Đề xuất: YES, làm ở P3 sau khi endpoint stable.**

---

## 9. Files & checklist khi migrate 1 chart hiện có sang DataContract

```
☐ Tạo {Domain}Row.cs record trong Contracts/DataContracts/{Domain}/
☐ Tạo {Domain}Contract.cs class trong cùng folder
☐ (Optional) Tạo {Domain}Validator.cs trong cùng folder
☐ Tạo {Domain}{SourceKind}Source.cs trong {Service}.Infrastructure/DataContracts/Sources/
   ↳ Copy nguyên logic fetch từ SduiPageConfig.BuildPage hoặc ILakehouseChartConfig.BuildAsync
   ↳ Convert output → IAsyncEnumerable<{Domain}Row>
☐ Tạo {Domain}{ConsumerKind}Consumer.cs trong {Service}.Infrastructure/DataContracts/Consumers/
   ↳ Copy nguyên logic build SduiPage
   ↳ Input đổi từ raw payload sang stream {Domain}Row
☐ Đăng ký DI trong DataContractsRegistration.cs:
     services.AddDataContract<{Domain}Contract>()
             .AddDataSource<{Domain}Row, {Domain}{SourceKind}Source>()
             .AddDataConsumer<{Domain}Row, SduiPage, {Domain}{ConsumerKind}Consumer>();
☐ Test endpoint mới /lakehouse/contracts/{contract.code}/chart trả SduiPage shape đúng
☐ Endpoint cũ /lakehouse/charts/{code} GIỮ NGUYÊN — không xóa trong session migrate
☐ Update doc cho chart mới (nếu có doc riêng)
```

---

## 10. Status & roadmap

| Phase | Trạng thái | File / commit |
|---|---|---|
| P1 — Contract layer | ✅ DONE 2026-06-09 | `src/BuildingBlocks/Contracts/DataContracts/*` |
| P2 — FinanceDaily pilot | ✅ DONE 2026-06-09 | `src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/*` |
| P3 — Endpoint mới + flag | ✅ DONE 2026-06-09 | `DataContractChartController.cs` |
| P4 — DynamicForm consumer | ✅ DONE 2026-06-09 | `DataContractFormBindingResolver.cs` |
| P5 — DataMatching ingest wrap | ✅ DONE 2026-06-09 | `IngestThroughContractAsync` extension |
| P6 — `[Obsolete]` markers | ✅ DONE 2026-06-09 | annotations trên class cũ |
| P7 — Tests | ✅ DONE 2026-06-09 | `tests/Hdos.BuildingBlocks.Tests/DataContracts/` |
| Hard delete cũ | ⏳ TODO sau khi prod stable | session khác |

---

## Companion docs

- **doc 44** — Unified Ingest Pipeline. SourceProfile + IngestCoreService — base của P5.
- **doc 48** — FE consume SDUI chart. FE không thay đổi khi BE migrate sang DataContract (cùng JSON shape).
- **doc 49** — Path A recipe.
- **doc 50** — Path B recipe.
- **doc 51** — System overview. Đọc trước doc này nếu chưa quen Path A/B.
- **doc 52** — Embed chart vào DynamicForm. Provider/Operation catalog ăn khớp với DataContract namespace.
