using Hdos.LakehouseService.Application.Charts.Sdui;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.Charts.Configs;

/// <summary>
/// Tài chính theo ngày — query trực tiếp <b>raw tables</b> lakehouse PG, KHÔNG qua view.
/// Mỗi widget compute bằng SQL JOIN + GROUP BY ở DB-side.
///
/// ⚠️ TRƯỚC KHI DEPLOY: Thay placeholder table names trong const dưới đây + cột thật
/// trong các SQL bằng schema/column thực tế của bạn.
///   Grep từ khóa: "TODO_TABLE", "TODO_COLUMN" trong file này.
///
/// Canonical alias trong SQL match SourceProfile convention nếu có:
///   total_amount      → TotalInvoiceAmount
///   discount_amount   → TotalDiscountAmount
///   invoice_count     → InvoiceCount
///   encounter_count   → DistinctEncounterCount
///   department_id     → DepartmentId
///   department_name   → DepartmentName     (mới — JOIN từ master table)
///   finance_bucket    → FinanceBucket
///
/// GET /lakehouse/charts/finance-daily?date=yyyy-MM-dd&amp;department=3
/// </summary>
#pragma warning disable CS0618 // Soft deprecated (doc 53 P6) — chuyển sang DataContract khi tới P7
public sealed class FinanceDailyLakehouseChart : ILakehouseChartConfig
{
    public string Code => "finance-daily";

    public const string SourceSystem = "lakehouse:finance_daily";
    public const string RecordType   = "finance-daily";

    // ── TODO_TABLE: replace với schema/table thật của bạn ──
    private const string InvoiceTable    = "raw.invoices";             // TODO_TABLE: vd "bronze.invoice_facts"
    private const string DepartmentTable = "master.departments";       // TODO_TABLE: vd "ref.departments"
    private const string EncounterTable  = "raw.encounters";           // TODO_TABLE: optional, comment-out nếu không có

    public async Task<SduiPage> BuildAsync(
        NpgsqlDataSource  ds,
        DateOnly          reportDate,
        IQueryCollection  query,
        CancellationToken ct)
    {
        // Demo mode — fake SduiPage không đụng DB. Dùng để FE test mapping
        // hoặc khi raw tables chưa setup. /lakehouse/charts/finance-daily?demo=true
        if (string.Equals(query["demo"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase))
            return BuildDemo(reportDate);

        int? deptId = int.TryParse(query["department"].FirstOrDefault(), out var d) ? d : null;

        var totals    = await FetchTotalsAsync(ds, reportDate, deptId, ct);
        if (totals.TotalInvoiceAmount == 0m && totals.InvoiceCount == 0)
            return BuildEmpty(reportDate, deptId);

        var perDept   = await FetchPerDepartmentAsync(ds, reportDate, deptId, ct);
        var perBucket = await FetchPerBucketAsync(ds, reportDate, deptId, ct);

        return new SduiPage(
            Code:        Code,
            Title:       "Tài chính theo ngày (Live, raw SQL)",
            Badge:       "Live",
            Live:        true,
            Subtitle:    $"Lakehouse trực tiếp · {DateTime.UtcNow.AddHours(7):HH:mm} · Ngày {reportDate:dd/MM/yyyy}"
                       + (deptId is null ? "" : $" · Khoa #{deptId}"),
            Actions:     [new("Xuất Excel", "default", null)],
            Rows:        [
                BuildKpiRow(totals),
                BuildProgressAndAlertRow(perDept),
                BuildFlowAndPieRow(totals, perBucket),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    // ─────────────────────────────────────────────────────────
    // Record types — alias canonical
    // ─────────────────────────────────────────────────────────

    private sealed record Totals(
        decimal TotalInvoiceAmount,
        decimal TotalDiscountAmount,
        int     InvoiceCount,
        int     DistinctEncounterCount);

    private sealed record PerDept(
        int     DepartmentId,
        string  DepartmentName,
        decimal TotalInvoiceAmount,
        decimal TotalDiscountAmount,
        int     DistinctEncounterCount);

    private sealed record PerBucket(
        string  FinanceBucket,
        decimal TotalInvoiceAmount);

    // ─────────────────────────────────────────────────────────
    // SQL queries — raw tables, không qua view
    // ─────────────────────────────────────────────────────────

    private static async Task<Totals> FetchTotalsAsync(
        NpgsqlDataSource ds, DateOnly date, int? deptFilter, CancellationToken ct)
    {
        // [Query 1/3 — Aggregate tổng từ invoices + JOIN encounters cho distinct count]
        //
        // TODO_COLUMN: rename cột nếu schema thực tế khác:
        //   i.invoice_date        — cột date của invoice
        //   i.department_id       — FK khoa
        //   i.total_amount        — số tiền trước discount
        //   i.discount_amount     — số tiền giảm
        //   e.encounter_id        — id lượt khám
        //   e.encounter_date      — ngày khám
        var sql = $"""
            SELECT
                COALESCE(SUM(i.total_amount),    0)::numeric            AS "TotalInvoiceAmount",
                COALESCE(SUM(i.discount_amount), 0)::numeric            AS "TotalDiscountAmount",
                COUNT(i.id)::int                                          AS "InvoiceCount",
                COUNT(DISTINCT e.encounter_id)::int                       AS "DistinctEncounterCount"
            FROM {InvoiceTable} i
            LEFT JOIN {EncounterTable} e
                   ON e.department_id   = i.department_id
                  AND e.encounter_date  = i.invoice_date
            WHERE i.invoice_date = @d
              AND (@dept IS NULL OR i.department_id = @dept)
        """;

        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@d",    date);
        cmd.Parameters.AddWithValue("@dept", (object?)deptFilter ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new Totals(0m, 0m, 0, 0);

        return new Totals(
            TotalInvoiceAmount:     reader.GetDecimal(reader.GetOrdinal("TotalInvoiceAmount")),
            TotalDiscountAmount:    reader.GetDecimal(reader.GetOrdinal("TotalDiscountAmount")),
            InvoiceCount:           reader.GetInt32(reader.GetOrdinal("InvoiceCount")),
            DistinctEncounterCount: reader.GetInt32(reader.GetOrdinal("DistinctEncounterCount")));
    }

    private static async Task<List<PerDept>> FetchPerDepartmentAsync(
        NpgsqlDataSource ds, DateOnly date, int? deptFilter, CancellationToken ct)
    {
        // [Query 2/3 — GROUP BY khoa, JOIN departments lấy tên khoa thật]
        //
        // TODO_COLUMN: rename cột master table nếu cần:
        //   d.id                  — PK departments
        //   d.department_name     — cột tên khoa
        //   (hoặc dùng d.name nếu schema khác)
        var sql = $"""
            SELECT
                i.department_id                                          AS "DepartmentId",
                COALESCE(d.department_name, 'Khoa #' || i.department_id) AS "DepartmentName",
                COALESCE(SUM(i.total_amount),    0)::numeric              AS "TotalInvoiceAmount",
                COALESCE(SUM(i.discount_amount), 0)::numeric              AS "TotalDiscountAmount",
                COUNT(DISTINCT e.encounter_id)::int                       AS "DistinctEncounterCount"
            FROM {InvoiceTable} i
            LEFT JOIN {DepartmentTable} d ON d.id = i.department_id
            LEFT JOIN {EncounterTable}  e ON e.department_id = i.department_id
                                          AND e.encounter_date = i.invoice_date
            WHERE i.invoice_date = @d
              AND (@dept IS NULL OR i.department_id = @dept)
            GROUP BY i.department_id, d.department_name
            ORDER BY SUM(i.total_amount) DESC NULLS LAST
            LIMIT 30
        """;

        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@d",    date);
        cmd.Parameters.AddWithValue("@dept", (object?)deptFilter ?? DBNull.Value);

        var list = new List<PerDept>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new PerDept(
                DepartmentId:           reader.GetInt32(reader.GetOrdinal("DepartmentId")),
                DepartmentName:         reader.GetString(reader.GetOrdinal("DepartmentName")),
                TotalInvoiceAmount:     reader.GetDecimal(reader.GetOrdinal("TotalInvoiceAmount")),
                TotalDiscountAmount:    reader.GetDecimal(reader.GetOrdinal("TotalDiscountAmount")),
                DistinctEncounterCount: reader.GetInt32(reader.GetOrdinal("DistinctEncounterCount"))));
        return list;
    }

    private static async Task<List<PerBucket>> FetchPerBucketAsync(
        NpgsqlDataSource ds, DateOnly date, int? deptFilter, CancellationToken ct)
    {
        // [Query 3/3 — GROUP BY loại hóa đơn]
        //
        // TODO_COLUMN: cột phân loại invoice — đổi tùy schema:
        //   i.invoice_type   — categorical
        //   hoặc i.bucket    — bucket name nếu đã có
        //   hoặc CASE WHEN ... THEN ... — tự compute
        var sql = $"""
            SELECT
                COALESCE(i.invoice_type, '(không có)')              AS "FinanceBucket",
                COALESCE(SUM(i.total_amount), 0)::numeric           AS "TotalInvoiceAmount"
            FROM {InvoiceTable} i
            WHERE i.invoice_date = @d
              AND (@dept IS NULL OR i.department_id = @dept)
            GROUP BY i.invoice_type
            HAVING SUM(i.total_amount) > 0
            ORDER BY SUM(i.total_amount) DESC
            LIMIT 10
        """;

        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@d",    date);
        cmd.Parameters.AddWithValue("@dept", (object?)deptFilter ?? DBNull.Value);

        var list = new List<PerBucket>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new PerBucket(
                FinanceBucket:      reader.GetString(reader.GetOrdinal("FinanceBucket")),
                TotalInvoiceAmount: reader.GetDecimal(reader.GetOrdinal("TotalInvoiceAmount"))));
        return list;
    }

    // ─────────────────────────────────────────────────────────
    // Section builders — không đổi vs version gọi view
    // ─────────────────────────────────────────────────────────

    private static SduiRow BuildKpiRow(Totals t)
    {
        decimal net          = t.TotalInvoiceAmount - t.TotalDiscountAmount;
        decimal discountRate = t.TotalInvoiceAmount > 0 ? Math.Round(t.TotalDiscountAmount * 100m / t.TotalInvoiceAmount, 1) : 0;

        return new SduiRow([
            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Tổng doanh thu",
                Value:  FormatVnd(t.TotalInvoiceAmount),
                Accent: "#1677ff", Hint: "VNĐ", HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Tổng giảm giá",
                Value:  FormatVnd(t.TotalDiscountAmount),
                Accent: discountRate >= 30 ? "#ff4d4f" : discountRate >= 15 ? "#faad14" : "#52c41a",
                Hint:   $"{discountRate}% DT", HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Số hóa đơn",
                Value:  t.InvoiceCount,
                Accent: "#722ed1",
                Hint:   $"DT thực: {FormatVnd(net)}", HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Lượt khám",
                Value:  t.DistinctEncounterCount,
                Accent: "#13c2c2",
                Hint:   t.DistinctEncounterCount > 0
                          ? $"AVG: {FormatVnd(t.TotalInvoiceAmount / t.DistinctEncounterCount)}/lượt"
                          : "—",
                HintColor: null)),
        ]);
    }

    private static SduiRow BuildProgressAndAlertRow(List<PerDept> rows)
    {
        decimal maxInvoice = rows.Count > 0 ? rows.Max(x => x.TotalInvoiceAmount) : 1;

        var items = rows
            .Take(15)
            .Select(x =>
            {
                double  pct      = maxInvoice > 0 ? Math.Round((double)(x.TotalInvoiceAmount * 100m / maxInvoice), 1) : 0;
                decimal discRate = x.TotalInvoiceAmount > 0 ? x.TotalDiscountAmount * 100m / x.TotalInvoiceAmount : 0;
                return new ProgressItem(
                    Label:          $"{x.DepartmentName} ({FormatShort(x.TotalInvoiceAmount)})",
                    Value:          pct,
                    SecondaryValue: null,
                    Color:          discRate >= 30 ? "#ff4d4f"
                                  : discRate >= 15 ? "#faad14"
                                  :                   "#52c41a");
            })
            .ToList();

        var progress = new ProgressListComponent(16, new ProgressListProps(
            Title:         "Top 15 khoa theo doanh thu",
            HeaderAction:  null,
            MaxValue:      100,
            Items:         items,
            FooterActions: null));

        var alerts = rows
            .Where(x => x.TotalInvoiceAmount > 0)
            .Select(x => (x.DepartmentId, x.DepartmentName, x.TotalInvoiceAmount, x.TotalDiscountAmount,
                          Pct: x.TotalDiscountAmount * 100m / x.TotalInvoiceAmount))
            .Where(x => x.Pct >= 30)
            .OrderByDescending(x => x.Pct)
            .Take(20)
            .Select(x => new AlertItem(
                Code:     $"K#{x.DepartmentId}",
                Text:     $"Giảm {Math.Round(x.Pct, 1)}% — {FormatShort(x.TotalDiscountAmount)} / {FormatShort(x.TotalInvoiceAmount)}",
                Patient:  "—",
                Dept:     x.DepartmentName,
                Time:     "hôm nay",
                Severity: x.Pct >= 50 ? "critical" : "warning"))
            .ToList();

        var alertList = new AlertListComponent(8, new AlertListProps(
            Title:         "Khoa giảm giá cao",
            RealtimeBadge: true,
            MaxHeight:     400,
            TotalCount:    alerts.Count,
            Items:         alerts));

        return new SduiRow([progress, alertList]);
    }

    private static SduiRow BuildFlowAndPieRow(Totals t, List<PerBucket> buckets)
    {
        decimal net = t.TotalInvoiceAmount - t.TotalDiscountAmount;

        var flow = new FlowPipelineComponent(12, new FlowPipelineProps(
            Title:  "Dòng doanh thu",
            Footer: $"Tỉ lệ giảm: {(t.TotalInvoiceAmount > 0 ? Math.Round(t.TotalDiscountAmount * 100m / t.TotalInvoiceAmount, 1) : 0)}%",
            Stages: [
                new("Doanh thu", (int)(t.TotalInvoiceAmount  / 1_000_000m), "#1677ff"),
                new("Giảm giá",  (int)(t.TotalDiscountAmount / 1_000_000m), "#faad14"),
                new("DT thực",   (int)(net                   / 1_000_000m), net < 0 ? "#ff4d4f" : "#52c41a"),
            ]));

        var pieData = buckets
            .Select(b => new ChartPieData(b.FinanceBucket, (double)b.TotalInvoiceAmount))
            .ToList();

        var pie = new ChartPieComponent(12, new ChartPieProps(
            Title:   "Phân bổ doanh thu theo loại hóa đơn",
            Height:  280,
            Variant: "donut",
            Legend:  true,
            Data:    pieData,
            Colors:  ["#1677ff", "#52c41a", "#faad14", "#ff4d4f", "#722ed1", "#13c2c2", "#eb2f96", "#fa8c16", "#8c8c8c"]));

        return new SduiRow([flow, pie]);
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    private static string FormatVnd(decimal v) =>
        Math.Abs(v) >= 1_000_000_000m ? $"{v / 1_000_000_000m:0.##} tỷ"
        : Math.Abs(v) >= 1_000_000m   ? $"{v / 1_000_000m:0.##} tr"
        : $"{v:N0} đ";

    private static string FormatShort(decimal v) =>
        Math.Abs(v) >= 1_000_000_000m ? $"{v / 1_000_000_000m:0.#}T"
        : Math.Abs(v) >= 1_000_000m   ? $"{v / 1_000_000m:0.#}tr"
        : Math.Abs(v) >= 1_000m       ? $"{v / 1_000m:0}k"
        : $"{v:N0}";

    private SduiPage BuildEmpty(DateOnly reportDate, int? deptId) =>
        new(
            Code, "Tài chính theo ngày (Live, raw SQL)", "Trống", false,
            $"Không có dữ liệu ngày {reportDate:dd/MM/yyyy}"
            + (deptId is null ? "" : $" cho khoa #{deptId}") + ". "
            + "Kiểm tra TODO_TABLE/TODO_COLUMN trong chart code có khớp schema thật.",
            [], [], DateTime.UtcNow);

    // ─────────────────────────────────────────────────────────────────────
    // Demo mode — fake SduiPage không query DB. Dùng để verify shape +
    // FE test mapping mà không phụ thuộc raw tables đã setup hay chưa.
    // ─────────────────────────────────────────────────────────────────────
    private SduiPage BuildDemo(DateOnly reportDate)
    {
        var fakeTotals = new Totals(
            TotalInvoiceAmount:     5_240_000_000m,   // 5.24 tỷ
            TotalDiscountAmount:      890_000_000m,   //  890 tr
            InvoiceCount:           1_247,
            DistinctEncounterCount:   832);

        var fakeDepts = new List<PerDept>
        {
            new(DepartmentId: 1, DepartmentName: "Khoa Tim mạch",
                TotalInvoiceAmount: 1_240_000_000m, TotalDiscountAmount: 180_000_000m, DistinctEncounterCount: 156),
            new(DepartmentId: 2, DepartmentName: "Khoa Hồi sức tích cực",
                TotalInvoiceAmount:   980_000_000m, TotalDiscountAmount: 320_000_000m, DistinctEncounterCount:  98),
            new(DepartmentId: 3, DepartmentName: "Khoa Nhi",
                TotalInvoiceAmount:   720_000_000m, TotalDiscountAmount:  85_000_000m, DistinctEncounterCount: 142),
            new(DepartmentId: 4, DepartmentName: "Khoa Sản",
                TotalInvoiceAmount:   650_000_000m, TotalDiscountAmount:  72_000_000m, DistinctEncounterCount: 124),
            new(DepartmentId: 5, DepartmentName: "Khoa Cấp cứu",
                TotalInvoiceAmount:   520_000_000m, TotalDiscountAmount: 280_000_000m, DistinctEncounterCount: 186),  // discount 53% — alert
            new(DepartmentId: 6, DepartmentName: "Khoa Ngoại thần kinh",
                TotalInvoiceAmount:   480_000_000m, TotalDiscountAmount:  35_000_000m, DistinctEncounterCount:  47),
            new(DepartmentId: 7, DepartmentName: "Khoa Nội tiết",
                TotalInvoiceAmount:   420_000_000m, TotalDiscountAmount:  18_000_000m, DistinctEncounterCount:  51),
            new(DepartmentId: 8, DepartmentName: "Khoa Da liễu",
                TotalInvoiceAmount:   230_000_000m, TotalDiscountAmount:   8_000_000m, DistinctEncounterCount:  28),
        };

        var fakeBuckets = new List<PerBucket>
        {
            new("BHYT",            2_650_000_000m),
            new("Dịch vụ",         1_420_000_000m),
            new("Yêu cầu",           680_000_000m),
            new("Bảo hiểm tư",       350_000_000m),
            new("Khác",              140_000_000m),
        };

        return new SduiPage(
            Code:        Code,
            Title:       "Tài chính theo ngày (Demo)",
            Badge:       "Demo",
            Live:        false,
            Subtitle:    $"⚠ DEMO MODE — fake data, không từ lakehouse · Ngày {reportDate:dd/MM/yyyy}",
            Actions:     [new("Xuất Excel", "default", null)],
            Rows:        [
                BuildKpiRow(fakeTotals),
                BuildProgressAndAlertRow(fakeDepts),
                BuildFlowAndPieRow(fakeTotals, fakeBuckets),
            ],
            GeneratedAt: DateTime.UtcNow);
    }
}
