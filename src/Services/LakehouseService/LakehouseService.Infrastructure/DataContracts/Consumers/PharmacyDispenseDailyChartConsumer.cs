using Hdos.Contracts.DataContracts;
using Hdos.LakehouseService.Application.Charts.Sdui;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Pharmacy;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Consumers;

/// <summary>
/// Consumer: stream <see cref="PharmacyDispenseDailyRow"/> → <see cref="SduiPage"/>.
///
/// Layout 3 row × 24 col:
///   Row 1 — 4 KpiCard (span 6×4):
///             Tổng đơn · Tổng liều · Tổng giá trị · % Kháng sinh (alert >40%)
///   Row 2 — ProgressList (span 16) + AlertList (span 8):
///             Top 15 khoa theo DoseCount, màu theo % kháng sinh
///             Khoa StockAlertLevel ∈ {low, out} hoặc % KS >50%
///   Row 3 — FlowPipeline (span 12) + ChartPie donut (span 12):
///             Kê đơn → Cấp phát → BN nhận
///             Phân bổ DoseCount theo nhóm thuốc
///
/// ⚠️ Note: PatientServedCount khi sum across (dept × group) rows over-count
/// nếu 1 BN nhận nhiều nhóm thuốc. Approximation chấp nhận được cho pilot
/// (giống cách FinanceDailyChartConsumer handle DistinctEncounterCount).
/// </summary>
public sealed class PharmacyDispenseDailyChartConsumer
    : IDataConsumer<PharmacyDispenseDailyRow, SduiPage>
{
    public string ContractCode => PharmacyDispenseDailyContract.ContractCode;
    public string ConsumerCode => "chart";

    public async Task<SduiPage> ConsumeAsync(
        IAsyncEnumerable<PharmacyDispenseDailyRow> stream,
        DataContractQuery query,
        CancellationToken ct)
    {
        var rows = new List<PharmacyDispenseDailyRow>();
        await foreach (var row in stream.WithCancellation(ct))
            rows.Add(row);

        var date   = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var deptId = query.GetInt("department");

        if (rows.Count == 0)
            return BuildEmpty(date, deptId);

        var totals    = AggregateTotals(rows);
        var perDept   = AggregatePerDept(rows);
        var perGroup  = AggregatePerGroup(rows);

        return new SduiPage(
            Code:        "pharmacy-dispense-daily",
            Title:       "Phát thuốc theo ngày (DataContract)",
            Badge:       "Contract",
            Live:        true,
            Subtitle:    $"Qua DataContractGateway · {DateTime.UtcNow.AddHours(7):HH:mm} · Ngày {date:dd/MM/yyyy}"
                       + (deptId is null ? "" : $" · Khoa #{deptId}"),
            Actions:     [new("Xuất Excel", "default", null)],
            Rows: [
                BuildKpiRow(totals),
                BuildProgressAndAlertRow(perDept),
                BuildFlowAndPieRow(totals, perGroup),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Aggregations
    // ─────────────────────────────────────────────────────────────────────

    private sealed record Totals(
        int     PrescriptionCount,
        int     DoseCount,
        decimal TotalAmount,
        int     PatientServedCount,
        int     AntibioticDoseCount);

    private sealed record DeptAgg(
        int     DepartmentId,
        string  DepartmentName,
        string? StockAlertLevel,
        int     DoseCount,
        int     AntibioticDoseCount,
        decimal TotalAmount);

    private sealed record GroupAgg(string DrugGroup, int DoseCount);

    private static Totals AggregateTotals(IReadOnlyList<PharmacyDispenseDailyRow> rows) =>
        new(
            PrescriptionCount:    rows.Sum(r => r.PrescriptionCount),
            DoseCount:            rows.Sum(r => r.DoseCount),
            TotalAmount:          rows.Sum(r => r.TotalAmount),
            PatientServedCount:   rows.Sum(r => r.PatientServedCount),
            AntibioticDoseCount:  rows.Where(IsAntibiotic).Sum(r => r.DoseCount));

    private static List<DeptAgg> AggregatePerDept(IReadOnlyList<PharmacyDispenseDailyRow> rows) =>
        rows.GroupBy(r => r.DepartmentId)
            .Select(g => new DeptAgg(
                DepartmentId:        g.Key,
                DepartmentName:      g.First().DepartmentName,
                StockAlertLevel:     g.First().StockAlertLevel,
                DoseCount:           g.Sum(r => r.DoseCount),
                AntibioticDoseCount: g.Where(IsAntibiotic).Sum(r => r.DoseCount),
                TotalAmount:         g.Sum(r => r.TotalAmount)))
            .OrderByDescending(d => d.DoseCount)
            .ToList();

    private static List<GroupAgg> AggregatePerGroup(IReadOnlyList<PharmacyDispenseDailyRow> rows) =>
        rows.GroupBy(r => r.DrugGroup)
            .Select(g => new GroupAgg(g.Key, g.Sum(r => r.DoseCount)))
            .Where(g => g.DoseCount > 0)
            .OrderByDescending(g => g.DoseCount)
            .ToList();

    private static bool IsAntibiotic(PharmacyDispenseDailyRow r) =>
        string.Equals(r.DrugGroup, "Kháng sinh", StringComparison.OrdinalIgnoreCase);

    // ─────────────────────────────────────────────────────────────────────
    // Section builders
    // ─────────────────────────────────────────────────────────────────────

    private static SduiRow BuildKpiRow(Totals t)
    {
        decimal antibioticPct = t.DoseCount > 0
            ? Math.Round(t.AntibioticDoseCount * 100m / t.DoseCount, 1) : 0;

        return new SduiRow([
            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Tổng đơn thuốc",
                Value:  t.PrescriptionCount,
                Accent: "#1677ff",
                Hint:   t.PatientServedCount > 0
                          ? $"{t.PatientServedCount} lượt phục vụ"
                          : "—",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Tổng liều phát",
                Value:  t.DoseCount,
                Accent: "#13c2c2",
                Hint:   t.PrescriptionCount > 0
                          ? $"AVG: {Math.Round((double)t.DoseCount / t.PrescriptionCount, 1)} liều/đơn"
                          : "—",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Tổng giá trị",
                Value:  FormatVnd(t.TotalAmount),
                Accent: "#722ed1",
                Hint:   "VNĐ",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:  "% Kháng sinh",
                Value:  $"{antibioticPct}%",
                Accent: antibioticPct >= 40 ? "#ff4d4f"
                      : antibioticPct >= 25 ? "#faad14" : "#52c41a",
                Hint:   $"{t.AntibioticDoseCount} liều KS",
                HintColor: null)),
        ]);
    }

    private static SduiRow BuildProgressAndAlertRow(List<DeptAgg> depts)
    {
        int maxDose = depts.Count > 0 ? depts.Max(x => x.DoseCount) : 1;

        var items = depts
            .Take(15)
            .Select(x =>
            {
                double pct = maxDose > 0
                    ? Math.Round((double)x.DoseCount * 100.0 / maxDose, 1) : 0;
                double abxRate = x.DoseCount > 0
                    ? (double)x.AntibioticDoseCount * 100.0 / x.DoseCount : 0;
                return new ProgressItem(
                    Label:          $"{x.DepartmentName} ({x.DoseCount:N0} liều)",
                    Value:          pct,
                    SecondaryValue: null,
                    Color:          abxRate >= 50 ? "#ff4d4f"
                                  : abxRate >= 30 ? "#faad14"
                                  :                  "#52c41a");
            })
            .ToList();

        var progress = new ProgressListComponent(16, new ProgressListProps(
            Title:         "Top 15 khoa theo số liều (màu = % kháng sinh)",
            HeaderAction:  null,
            MaxValue:      100,
            Items:         items,
            FooterActions: null));

        var alertItems = new List<AlertItem>();

        foreach (var x in depts.Where(d => !string.IsNullOrEmpty(d.StockAlertLevel)))
        {
            alertItems.Add(new AlertItem(
                Code:     $"K#{x.DepartmentId}",
                Text:     x.StockAlertLevel == "out"
                            ? "Hết thuốc — cần nhập gấp"
                            : "Tồn kho thấp",
                Patient:  "—",
                Dept:     x.DepartmentName,
                Time:     "hôm nay",
                Severity: x.StockAlertLevel == "out" ? "critical" : "warning"));
        }

        foreach (var x in depts.Where(d => d.DoseCount > 0))
        {
            double abxRate = (double)x.AntibioticDoseCount * 100.0 / x.DoseCount;
            if (abxRate < 50) continue;

            alertItems.Add(new AlertItem(
                Code:     $"K#{x.DepartmentId}",
                Text:     $"Kháng sinh {Math.Round(abxRate, 1)}% ({x.AntibioticDoseCount} liều)",
                Patient:  "—",
                Dept:     x.DepartmentName,
                Time:     "hôm nay",
                Severity: abxRate >= 65 ? "critical" : "warning"));
        }

        var alertList = new AlertListComponent(8, new AlertListProps(
            Title:         "Cảnh báo kho + lạm dụng kháng sinh",
            RealtimeBadge: true,
            MaxHeight:     400,
            TotalCount:    alertItems.Count,
            Items:         alertItems));

        return new SduiRow([progress, alertList]);
    }

    private static SduiRow BuildFlowAndPieRow(Totals t, List<GroupAgg> groups)
    {
        var flow = new FlowPipelineComponent(12, new FlowPipelineProps(
            Title:  "Dòng phát thuốc",
            Footer: t.PrescriptionCount > 0
                      ? $"AVG: {Math.Round((double)t.DoseCount / t.PrescriptionCount, 1)} liều/đơn"
                      : null,
            Stages: [
                new("Kê đơn",   t.PrescriptionCount,  "#1677ff"),
                new("Cấp phát", t.DoseCount,          "#13c2c2"),
                new("BN nhận",  t.PatientServedCount, "#52c41a"),
            ]));

        var pieData = groups
            .Select(g => new ChartPieData(g.DrugGroup, (double)g.DoseCount))
            .ToList();

        var pie = new ChartPieComponent(12, new ChartPieProps(
            Title:   "Phân bổ liều theo nhóm thuốc",
            Height:  280,
            Variant: "donut",
            Legend:  true,
            Data:    pieData,
            Colors:  ["#ff4d4f", "#1677ff", "#faad14", "#52c41a", "#722ed1", "#13c2c2", "#eb2f96"]));

        return new SduiRow([flow, pie]);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static SduiPage BuildEmpty(DateOnly date, int? deptId) =>
        new(
            Code:        "pharmacy-dispense-daily",
            Title:       "Phát thuốc theo ngày (DataContract)",
            Badge:       "Trống",
            Live:        false,
            Subtitle:    $"Không có dữ liệu ngày {date:dd/MM/yyyy}"
                       + (deptId is null ? "" : $" cho khoa #{deptId}") + ". "
                       + "Thử ?source=demo.",
            Actions:     [],
            Rows:        [],
            GeneratedAt: DateTime.UtcNow);

    private static string FormatVnd(decimal v) =>
        Math.Abs(v) >= 1_000_000_000m ? $"{v / 1_000_000_000m:0.##} tỷ"
        : Math.Abs(v) >= 1_000_000m   ? $"{v / 1_000_000m:0.##} tr"
        : $"{v:N0} đ";
}
