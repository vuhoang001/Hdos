using Hdos.LakehouseService.Application.Charts.Sdui;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.Charts.Configs;

/// <summary>
/// Tài chính theo ngày — query trực tiếp warehouse view <c>api.finance_daily</c>
/// bằng raw SQL Npgsql. KHÔNG qua StagingRecord / ingest.
///
/// SQL alias dùng đúng canonical names match SourceProfile
/// <c>lakehouse:finance_daily / finance-daily</c> đã đăng ký bên DataMatching:
///   total_invoice_amount     → TotalInvoiceAmount
///   total_discount_amount    → TotalDiscountAmount
///   invoice_count            → InvoiceCount
///   distinct_encounter_count → DistinctEncounterCount
///   department_id            → DepartmentId
///   finance_bucket           → FinanceBucket
///
/// Khi SourceProfile thay đổi tên canonical → sửa cả alias trong SQL ở đây.
///
/// GET /lakehouse/charts/finance-daily?date=yyyy-MM-dd&amp;department=3
/// </summary>
public sealed class FinanceDailyLakehouseChart : ILakehouseChartConfig
{
    public string Code => "finance-daily";

    // ── SourceProfile convention (tham chiếu, không enforce runtime) ─────
    public const string SourceSystem = "lakehouse:finance_daily";
    public const string RecordType   = "finance-daily";
    private const string ViewName    = "api.finance_daily";

    public async Task<SduiPage> BuildAsync(
        NpgsqlDataSource  ds,
        DateOnly          reportDate,
        IQueryCollection  query,
        CancellationToken ct)
    {
        int? deptId = int.TryParse(query["department"].FirstOrDefault(), out var d) ? d : null;

        var totals    = await FetchTotalsAsync(ds, reportDate, deptId, ct);
        if (totals.TotalInvoiceAmount == 0m && totals.InvoiceCount == 0)
            return BuildEmpty(reportDate, deptId);

        var perDept   = await FetchPerDepartmentAsync(ds, reportDate, deptId, ct);
        var perBucket = await FetchPerBucketAsync(ds, reportDate, deptId, ct);

        return new SduiPage(
            Code:        Code,
            Title:       "Tài chính theo ngày (Live)",
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
    // Raw SQL — alias = canonical name của SourceProfile
    // ─────────────────────────────────────────────────────────

    // Record fields dùng đúng canonical name → dev đọc SQL/C# nhất quán
    private sealed record Totals(
        decimal TotalInvoiceAmount,
        decimal TotalDiscountAmount,
        int     InvoiceCount,
        int     DistinctEncounterCount);

    private sealed record PerDept(
        int     DepartmentId,
        decimal TotalInvoiceAmount,
        decimal TotalDiscountAmount,
        int     DistinctEncounterCount);

    private sealed record PerBucket(
        string  FinanceBucket,
        decimal TotalInvoiceAmount);

    private static async Task<Totals> FetchTotalsAsync(
        NpgsqlDataSource ds, DateOnly date, int? deptFilter, CancellationToken ct)
    {
        const string sql = $"""
            SELECT
                COALESCE(SUM(total_invoice_amount),     0)::numeric AS "TotalInvoiceAmount",
                COALESCE(SUM(total_discount_amount),    0)::numeric AS "TotalDiscountAmount",
                COALESCE(SUM(invoice_count),            0)::int     AS "InvoiceCount",
                COALESCE(SUM(distinct_encounter_count), 0)::int     AS "DistinctEncounterCount"
            FROM {ViewName}
            WHERE date = @d
              AND (@dept IS NULL OR department_id = @dept)
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
        const string sql = $"""
            SELECT
                department_id                                       AS "DepartmentId",
                COALESCE(SUM(total_invoice_amount),     0)::numeric AS "TotalInvoiceAmount",
                COALESCE(SUM(total_discount_amount),    0)::numeric AS "TotalDiscountAmount",
                COALESCE(SUM(distinct_encounter_count), 0)::int     AS "DistinctEncounterCount"
            FROM {ViewName}
            WHERE date = @d
              AND (@dept IS NULL OR department_id = @dept)
            GROUP BY department_id
            ORDER BY SUM(total_invoice_amount) DESC NULLS LAST
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
                TotalInvoiceAmount:     reader.GetDecimal(reader.GetOrdinal("TotalInvoiceAmount")),
                TotalDiscountAmount:    reader.GetDecimal(reader.GetOrdinal("TotalDiscountAmount")),
                DistinctEncounterCount: reader.GetInt32(reader.GetOrdinal("DistinctEncounterCount"))));
        return list;
    }

    private static async Task<List<PerBucket>> FetchPerBucketAsync(
        NpgsqlDataSource ds, DateOnly date, int? deptFilter, CancellationToken ct)
    {
        const string sql = $"""
            SELECT
                COALESCE(finance_bucket, '(không có)')          AS "FinanceBucket",
                COALESCE(SUM(total_invoice_amount), 0)::numeric AS "TotalInvoiceAmount"
            FROM {ViewName}
            WHERE date = @d
              AND (@dept IS NULL OR department_id = @dept)
            GROUP BY finance_bucket
            HAVING SUM(total_invoice_amount) > 0
            ORDER BY SUM(total_invoice_amount) DESC
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
    // Section builders — code dùng canonical name nhất quán SourceProfile
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
                    Label:          $"Khoa #{x.DepartmentId} ({FormatShort(x.TotalInvoiceAmount)})",
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
            .Select(x => (x.DepartmentId, x.TotalInvoiceAmount, x.TotalDiscountAmount, Pct: x.TotalDiscountAmount * 100m / x.TotalInvoiceAmount))
            .Where(x => x.Pct >= 30)
            .OrderByDescending(x => x.Pct)
            .Take(20)
            .Select(x => new AlertItem(
                Code:     $"K#{x.DepartmentId}",
                Text:     $"Giảm {Math.Round(x.Pct, 1)}% — {FormatShort(x.TotalDiscountAmount)} / {FormatShort(x.TotalInvoiceAmount)}",
                Patient:  "—",
                Dept:     $"Khoa #{x.DepartmentId}",
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
            Code, "Tài chính theo ngày (Live)", "Trống", false,
            $"Không có dữ liệu ngày {reportDate:dd/MM/yyyy}"
            + (deptId is null ? "" : $" cho khoa #{deptId}") + ".",
            [], [], DateTime.UtcNow);
}
