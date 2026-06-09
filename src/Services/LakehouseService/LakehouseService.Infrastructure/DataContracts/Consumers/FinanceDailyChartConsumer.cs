using Hdos.Contracts.DataContracts;
using Hdos.Contracts.DataContracts.Finance;
using Hdos.LakehouseService.Application.Charts.Sdui;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Consumers;

/// <summary>
/// Consumer: stream <see cref="FinanceDailyRow"/> → <see cref="SduiPage"/> shape giống endpoint cũ
/// <c>/lakehouse/charts/finance-daily</c>. FE re-use renderer không thay đổi.
///
/// Tổng hợp:
///   - Totals: SUM across all rows (1 row × 4 KPI)
///   - PerDept: GROUP BY DepartmentId — top 15 progress + alert giảm giá ≥30%
///   - PerBucket: GROUP BY FinanceBucket — pie chart
///
/// ⚠️ Note: DistinctEncounterCount khi sum across (dept × bucket) rows có thể over-count
/// nếu 1 encounter span nhiều bucket. Đây là approximation chấp nhận được cho pilot.
/// </summary>
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
        await foreach (var row in stream.WithCancellation(ct))
            rows.Add(row);

        var date   = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var deptId = query.GetInt("department");

        if (rows.Count == 0)
            return BuildEmpty(date, deptId);

        var totals    = AggregateTotals(rows);
        var perDept   = AggregatePerDept(rows);
        var perBucket = AggregatePerBucket(rows);

        return new SduiPage(
            Code:        "finance-daily",
            Title:       "Tài chính theo ngày (DataContract)",
            Badge:       "Contract",
            Live:        true,
            Subtitle:    $"Qua DataContractGateway · {DateTime.UtcNow.AddHours(7):HH:mm} · Ngày {date:dd/MM/yyyy}"
                       + (deptId is null ? "" : $" · Khoa #{deptId}"),
            Actions:     [new("Xuất Excel", "default", null)],
            Rows: [
                BuildKpiRow(totals),
                BuildProgressAndAlertRow(perDept),
                BuildFlowAndPieRow(totals, perBucket),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Aggregations
    // ─────────────────────────────────────────────────────────────────────

    private sealed record Totals(
        decimal TotalInvoiceAmount,
        decimal TotalDiscountAmount,
        int     InvoiceCount,
        int     DistinctEncounterCount);

    private sealed record DeptAgg(
        int     DepartmentId,
        string  DepartmentName,
        decimal TotalInvoiceAmount,
        decimal TotalDiscountAmount,
        int     DistinctEncounterCount);

    private sealed record BucketAgg(
        string  FinanceBucket,
        decimal TotalInvoiceAmount);

    private static Totals AggregateTotals(IReadOnlyList<FinanceDailyRow> rows) =>
        new(
            TotalInvoiceAmount:     rows.Sum(r => r.TotalInvoiceAmount),
            TotalDiscountAmount:    rows.Sum(r => r.TotalDiscountAmount),
            InvoiceCount:           rows.Sum(r => r.InvoiceCount),
            DistinctEncounterCount: rows.Sum(r => r.DistinctEncounterCount));

    private static List<DeptAgg> AggregatePerDept(IReadOnlyList<FinanceDailyRow> rows) =>
        rows.GroupBy(r => r.DepartmentId)
            .Select(g => new DeptAgg(
                DepartmentId:           g.Key,
                DepartmentName:         g.First().DepartmentName,
                TotalInvoiceAmount:     g.Sum(r => r.TotalInvoiceAmount),
                TotalDiscountAmount:    g.Sum(r => r.TotalDiscountAmount),
                DistinctEncounterCount: g.Sum(r => r.DistinctEncounterCount)))
            .OrderByDescending(d => d.TotalInvoiceAmount)
            .ToList();

    private static List<BucketAgg> AggregatePerBucket(IReadOnlyList<FinanceDailyRow> rows) =>
        rows.Where(r => r.FinanceBucket is not null)
            .GroupBy(r => r.FinanceBucket!)
            .Select(g => new BucketAgg(g.Key, g.Sum(r => r.TotalInvoiceAmount)))
            .Where(b => b.TotalInvoiceAmount > 0)
            .OrderByDescending(b => b.TotalInvoiceAmount)
            .Take(10)
            .ToList();

    // ─────────────────────────────────────────────────────────────────────
    // Section builders
    // ─────────────────────────────────────────────────────────────────────

    private static SduiRow BuildKpiRow(Totals t)
    {
        decimal net          = t.TotalInvoiceAmount - t.TotalDiscountAmount;
        decimal discountRate = t.TotalInvoiceAmount > 0
            ? Math.Round(t.TotalDiscountAmount * 100m / t.TotalInvoiceAmount, 1) : 0;

        return new SduiRow([
            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Tổng doanh thu",
                Value:  FormatVnd(t.TotalInvoiceAmount),
                Accent: "#1677ff", Hint: "VNĐ", HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Tổng giảm giá",
                Value:  FormatVnd(t.TotalDiscountAmount),
                Accent: discountRate >= 30 ? "#ff4d4f"
                      : discountRate >= 15 ? "#faad14" : "#52c41a",
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

    private static SduiRow BuildProgressAndAlertRow(List<DeptAgg> depts)
    {
        decimal maxInvoice = depts.Count > 0 ? depts.Max(x => x.TotalInvoiceAmount) : 1;

        var items = depts
            .Take(15)
            .Select(x =>
            {
                double pct = maxInvoice > 0
                    ? Math.Round((double)(x.TotalInvoiceAmount * 100m / maxInvoice), 1) : 0;
                decimal discRate = x.TotalInvoiceAmount > 0
                    ? x.TotalDiscountAmount * 100m / x.TotalInvoiceAmount : 0;
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

        var alerts = depts
            .Where(x => x.TotalInvoiceAmount > 0)
            .Select(x => (
                x.DepartmentId, x.DepartmentName,
                x.TotalInvoiceAmount, x.TotalDiscountAmount,
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

    private static SduiRow BuildFlowAndPieRow(Totals t, List<BucketAgg> buckets)
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

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static SduiPage BuildEmpty(DateOnly date, int? deptId) =>
        new(
            Code:        "finance-daily",
            Title:       "Tài chính theo ngày (DataContract)",
            Badge:       "Trống",
            Live:        false,
            Subtitle:    $"Không có dữ liệu ngày {date:dd/MM/yyyy}"
                       + (deptId is null ? "" : $" cho khoa #{deptId}") + ". "
                       + "Kiểm tra source data hoặc thử ?source=demo.",
            Actions:     [],
            Rows:        [],
            GeneratedAt: DateTime.UtcNow);

    private static string FormatVnd(decimal v) =>
        Math.Abs(v) >= 1_000_000_000m ? $"{v / 1_000_000_000m:0.##} tỷ"
        : Math.Abs(v) >= 1_000_000m   ? $"{v / 1_000_000m:0.##} tr"
        : $"{v:N0} đ";

    private static string FormatShort(decimal v) =>
        Math.Abs(v) >= 1_000_000_000m ? $"{v / 1_000_000_000m:0.#}T"
        : Math.Abs(v) >= 1_000_000m   ? $"{v / 1_000_000m:0.#}tr"
        : Math.Abs(v) >= 1_000m       ? $"{v / 1_000m:0}k"
        : $"{v:N0}";
}
