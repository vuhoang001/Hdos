using Hdos.Contracts.DataContracts;
using Hdos.LakehouseService.Application.Charts.Sdui;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Finance;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Consumers;

// Stream FinanceMonthlyRow → SduiPage:
//   - Row 1: 4 KpiCard (annual totals)
//   - Row 2: ProgressList monthly trend (12 tháng) + ChartPie share per dept
//
// Query: ?year=&department=
public sealed class FinanceMonthlyChartConsumer : IDataConsumer<FinanceMonthlyRow, SduiPage>
{
    public string ContractCode => FinanceMonthlyContract.ContractCode;
    public string ConsumerCode => "chart";

    public async Task<SduiPage> ConsumeAsync(
        IAsyncEnumerable<FinanceMonthlyRow> stream,
        DataContractQuery                   query,
        CancellationToken                   ct)
    {
        var rows = new List<FinanceMonthlyRow>();
        await foreach (var r in stream.WithCancellation(ct))
            rows.Add(r);

        var year   = query.GetInt("year")       ?? DateTime.UtcNow.Year;
        var deptId = query.GetInt("department");

        if (rows.Count == 0)
            return BuildEmpty(year, deptId);

        var totals     = AggregateTotals(rows);
        var perMonth   = AggregatePerMonth(rows);
        var perDept    = AggregatePerDept(rows);

        return new SduiPage(
            Code:        "finance-monthly",
            Title:       "Tài chính theo tháng (DataContract)",
            Badge:       "Contract",
            Live:        true,
            Subtitle:    $"Năm {year}" + (deptId is null ? "" : $" · Khoa #{deptId}"),
            Actions:     [new("Xuất Excel", "default", null)],
            Rows: [
                BuildKpiRow(totals),
                BuildTrendAndPieRow(perMonth, perDept),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    private sealed record Totals(decimal Revenue, decimal Cost, int Patients);
    private sealed record MonthAgg(int Month, decimal Revenue);
    private sealed record DeptAgg(int Id, string Name, decimal Revenue);

    private static Totals AggregateTotals(IReadOnlyList<FinanceMonthlyRow> rows) =>
        new(
            Revenue:  rows.Sum(r => r.TotalRevenue),
            Cost:     rows.Sum(r => r.TotalCost),
            Patients: rows.Sum(r => r.PatientCount));

    private static List<MonthAgg> AggregatePerMonth(IReadOnlyList<FinanceMonthlyRow> rows) =>
        rows.GroupBy(r => r.Month)
            .Select(g => new MonthAgg(g.Key, g.Sum(r => r.TotalRevenue)))
            .OrderBy(x => x.Month)
            .ToList();

    private static List<DeptAgg> AggregatePerDept(IReadOnlyList<FinanceMonthlyRow> rows) =>
        rows.GroupBy(r => r.DepartmentId)
            .Select(g => new DeptAgg(g.Key, g.First().DepartmentName, g.Sum(r => r.TotalRevenue)))
            .OrderByDescending(d => d.Revenue)
            .ToList();

    private static SduiRow BuildKpiRow(Totals t)
    {
        var net          = t.Revenue - t.Cost;
        var marginPct    = t.Revenue > 0 ? Math.Round(net * 100m / t.Revenue, 1) : 0m;
        var avgPerPatient = t.Patients > 0 ? Math.Round(t.Revenue / t.Patients, 0) : 0m;

        return new SduiRow([
            new KpiCardComponent(6, new KpiCardProps(
                "Tổng doanh thu", FormatVnd(t.Revenue), "#1677ff", "VNĐ", null)),
            new KpiCardComponent(6, new KpiCardProps(
                "Tổng chi phí",   FormatVnd(t.Cost),    "#fa8c16", "VNĐ", null)),
            new KpiCardComponent(6, new KpiCardProps(
                "Lợi nhuận ròng", FormatVnd(net),
                net < 0 ? "#ff4d4f" : "#52c41a",
                $"Biên: {marginPct}%", null)),
            new KpiCardComponent(6, new KpiCardProps(
                "Lượt khám", t.Patients, "#13c2c2",
                $"AVG: {FormatVnd(avgPerPatient)}/lượt", null)),
        ]);
    }

    private static SduiRow BuildTrendAndPieRow(List<MonthAgg> months, List<DeptAgg> depts)
    {
        var maxRev = months.Count > 0 ? months.Max(m => m.Revenue) : 1m;
        var items = months
            .Select(m => new ProgressItem(
                Label:          $"Tháng {m.Month:D2} ({FormatShort(m.Revenue)})",
                Value:          maxRev > 0 ? Math.Round((double)(m.Revenue * 100m / maxRev), 1) : 0,
                SecondaryValue: null,
                Color:          m.Month is 1 or 12 ? "#1677ff"
                              : m.Month is >= 6 and <= 8 ? "#faad14"
                              : "#52c41a"))
            .ToList();

        var trend = new ProgressListComponent(16, new ProgressListProps(
            Title:        "Doanh thu theo tháng",
            HeaderAction: null,
            MaxValue:     100,
            Items:        items,
            FooterActions: null));

        var pie = new ChartPieComponent(8, new ChartPieProps(
            Title:   "Tỉ trọng doanh thu theo khoa",
            Height:  280,
            Variant: "donut",
            Legend:  true,
            Data:    depts.Select(d => new ChartPieData(d.Name, (double)d.Revenue)).ToList(),
            Colors:  ["#1677ff", "#52c41a", "#faad14", "#722ed1", "#13c2c2"]));

        return new SduiRow([trend, pie]);
    }

    private static SduiPage BuildEmpty(int year, int? deptId) =>
        new(
            Code:        "finance-monthly",
            Title:       "Tài chính theo tháng (DataContract)",
            Badge:       "Trống",
            Live:        false,
            Subtitle:    $"Không có dữ liệu năm {year}"
                       + (deptId is null ? "" : $" cho khoa #{deptId}") + ".",
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
