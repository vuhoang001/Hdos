using Hdos.Contracts.DataContracts;
using Hdos.LakehouseService.Application.Charts.Sdui;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Consumers;

/// <summary>
/// Consumer "chart": stream <see cref="PatientDailyNewRow"/> → SduiPage.
///
/// Output:
///   - Row 1: 4 KPI (Tổng BN, Tuổi TB weighted, Khoa đông nhất, Tỉ lệ nam TB)
///   - Row 2: ProgressList top 15 khoa (color theo AgeAvg: trẻ/trưởng thành/cao tuổi)
///   - Row 3: Donut phân bổ tuổi 4 bucket (xấp xỉ theo AgeAvg khoa)
/// </summary>
public sealed class PatientDailyNewChartConsumer
    : IDataConsumer<PatientDailyNewRow, SduiPage>
{
    public string ContractCode => PatientDailyNewContract.ContractCode;
    public string ConsumerCode => "chart";

    public async Task<SduiPage> ConsumeAsync(
        IAsyncEnumerable<PatientDailyNewRow> stream,
        DataContractQuery query,
        CancellationToken ct)
    {
        var rows = new List<PatientDailyNewRow>();
        await foreach (var r in stream.WithCancellation(ct)) rows.Add(r);

        var date    = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var totalBn = rows.Sum(r => r.NewPatientCount);

        if (totalBn == 0)
            return BuildEmpty(date);

        var weightedAge  = rows.Sum(r => r.AgeAvg * r.NewPatientCount);
        var avgAge       = weightedAge / totalBn;
        var weightedMale = rows.Sum(r => r.MalePct * r.NewPatientCount);
        var malePct      = weightedMale / totalBn;
        var topDept      = rows.OrderByDescending(r => r.NewPatientCount).First();

        return new SduiPage(
            Code:        "patient-daily-new",
            Title:       "Bệnh nhân đăng ký mới theo ngày",
            Badge:       "Contract",
            Live:        true,
            Subtitle:    $"Ngày {date:dd/MM/yyyy} · {rows.Count} khoa · qua DataContract",
            Actions:     [new("Xuất Excel", "default", null)],
            Rows: [
                BuildKpiRow(totalBn, avgAge, malePct, topDept),
                BuildProgressRow(rows),
                BuildAgeDistributionRow(rows),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Section builders
    // ─────────────────────────────────────────────────────────────────────

    private static SduiRow BuildKpiRow(
        int totalBn, double avgAge, double malePct, PatientDailyNewRow topDept) =>
        new([
            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Tổng BN mới",
                Value:  totalBn,
                Accent: "#1677ff",
                Hint:   "tất cả khoa",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Tuổi TB",
                Value:  $"{avgAge:F1}",
                Accent: avgAge < 18 ? "#52c41a" : avgAge > 60 ? "#faad14" : "#722ed1",
                Hint:   "weighted",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Khoa đông nhất",
                Value:  topDept.DepartmentName,
                Accent: "#13c2c2",
                Hint:   $"{topDept.NewPatientCount} BN",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:  "Tỉ lệ nam TB",
                Value:  $"{malePct:F1}%",
                Accent: malePct >= 50 ? "#1677ff" : "#eb2f96",
                Hint:   $"nữ: {100 - malePct:F1}%",
                HintColor: null)),
        ]);

    private static SduiRow BuildProgressRow(List<PatientDailyNewRow> rows)
    {
        var max = rows.Max(r => r.NewPatientCount);

        var items = rows
            .OrderByDescending(r => r.NewPatientCount)
            .Take(15)
            .Select(r => new ProgressItem(
                Label:          $"{r.DepartmentName} ({r.NewPatientCount} BN · TB {r.AgeAvg:F0} tuổi)",
                Value:          max > 0 ? Math.Round((double)r.NewPatientCount * 100 / max, 1) : 0,
                SecondaryValue: r.AgeAvg,
                Color:          r.AgeAvg < 18  ? "#52c41a"
                              : r.AgeAvg > 60  ? "#faad14"
                              :                  "#1677ff"))
            .ToList();

        return new SduiRow([
            new ProgressListComponent(24, new ProgressListProps(
                Title:         "Top khoa theo số BN mới (màu theo tuổi TB)",
                HeaderAction:  null,
                MaxValue:      100,
                Items:         items,
                FooterActions: null)),
        ]);
    }

    private static SduiRow BuildAgeDistributionRow(List<PatientDailyNewRow> rows)
    {
        var buckets = new[]
        {
            ("<18 tuổi (trẻ em)",   rows.Where(r => r.AgeAvg <  18).Sum(r => r.NewPatientCount)),
            ("18-40 tuổi",          rows.Where(r => r.AgeAvg >= 18 && r.AgeAvg < 40).Sum(r => r.NewPatientCount)),
            ("40-60 tuổi",          rows.Where(r => r.AgeAvg >= 40 && r.AgeAvg < 60).Sum(r => r.NewPatientCount)),
            (">=60 tuổi (cao tuổi)", rows.Where(r => r.AgeAvg >= 60).Sum(r => r.NewPatientCount)),
        };

        var pie = new ChartPieComponent(24, new ChartPieProps(
            Title:   "Phân bổ tuổi (xấp xỉ theo tuổi TB khoa)",
            Height:  280,
            Variant: "donut",
            Legend:  true,
            Data:    buckets.Where(b => b.Item2 > 0)
                            .Select(b => new ChartPieData(b.Item1, b.Item2))
                            .ToList(),
            Colors:  ["#52c41a", "#1677ff", "#722ed1", "#faad14"]));

        return new SduiRow([pie]);
    }

    private static SduiPage BuildEmpty(DateOnly date) => new(
        Code:        "patient-daily-new",
        Title:       "Bệnh nhân đăng ký mới",
        Badge:       "Trống",
        Live:        false,
        Subtitle:    $"Không có bệnh nhân mới ngày {date:dd/MM/yyyy}.",
        Actions:     [],
        Rows:        [],
        GeneratedAt: DateTime.UtcNow);
}
