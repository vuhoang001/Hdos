using System.Text.Json;

namespace Hdos.DataMatchingService.Application.Sdui.Pages;

/// <summary>
/// Dashboard công suất giường theo khoa — ingest từ Lakehouse view <c>api.bed_occupancy</c>
/// qua MVP B (with-auto-profile). Xem doc 45/47.
/// GET /dm/pages/bed-occupancy?date=yyyy-MM-dd
/// </summary>
#pragma warning disable CS0618 // Soft deprecated (doc 53 P6) — chuyển sang DataContract khi tới P7
public sealed class BedOccupancySduiConfig : SduiPageConfig
{
    public override string Code => "bed-occupancy";

    public override IReadOnlyList<string> RecordTypes => ["bed-occupancy"];

    public override SduiPage BuildPage(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate)
    {
        var rows = data.GetValueOrDefault("bed-occupancy", []);

        if (rows.Count == 0)
            return BuildEmpty(reportDate);

        // Lọc theo ngày báo cáo nếu data có nhiều ngày
        var todayRows = rows
            .Where(r => DateOnlyOf(r, "Date") == reportDate)
            .ToList();
        var effective = todayRows.Count > 0 ? todayRows : rows;   // fallback: dùng tất cả nếu không có ngày khớp

        int planned   = effective.Sum(r => Int(r, "PlannedBedCount"));
        int actual    = effective.Sum(r => Int(r, "ActualBedCount"));
        int occupied  = effective.Sum(r => Int(r, "OccupiedBedCount"));
        int available = effective.Sum(r => Int(r, "AvailableBedCount"));
        int disabled  = effective.Sum(r => Int(r, "DisabledBedCount"));
        double bor = actual > 0 ? Math.Round(occupied * 100.0 / actual, 1) : 0;

        return new SduiPage(
            Code:        Code,
            Title:       "Công suất giường bệnh",
            Badge:       todayRows.Count > 0 ? "Đúng ngày" : "Mới nhất",
            Live:        true,
            Subtitle:    $"Cập nhật {DateTime.UtcNow.AddHours(7):HH:mm} · Ngày {reportDate:dd/MM/yyyy} · {effective.Count} khoa",
            Actions:     [
                new("Xuất Excel", "default", null),
                new("Cài đặt",   "default", null),
            ],
            Rows:        [
                BuildKpiRow(planned, actual, occupied, available, bor),
                BuildMiddleRow(effective),
                BuildBottomRow(effective, occupied, available, disabled),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    // ─────────────────────────────────────────────────────────
    // Section builders
    // ─────────────────────────────────────────────────────────

    private static SduiRow BuildKpiRow(int planned, int actual, int occupied, int available, double bor) =>
        new([
            new KpiCardComponent(6, new KpiCardProps(
                Title:     "Tổng giường (Actual)",
                Value:     actual,
                Accent:    "#1677ff",
                Hint:      $"Kế hoạch: {planned}",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:     "Đang sử dụng",
                Value:     occupied,
                Accent:    "#52c41a",
                Hint:      "giường",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:     "Còn trống",
                Value:     available,
                Accent:    "#faad14",
                Hint:      "khả dụng",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:     "BOR",
                Value:     $"{bor}%",
                Accent:    bor >= 90 ? "#ff4d4f" : bor >= 75 ? "#faad14" : "#52c41a",
                Hint:      "công suất",
                HintColor: null)),
        ]);

    private static SduiRow BuildMiddleRow(List<Dictionary<string, JsonElement>> rows)
    {
        // ProgressList: BOR theo khoa
        var items = rows
            .Select(r =>
            {
                var khoa = Str(r, "KhoaDieuTri") ?? Str(r, "DepartmentCode") ?? "(không tên)";
                int occ  = Int(r, "OccupiedBedCount");
                int act  = Int(r, "ActualBedCount");
                double pct = act > 0 ? Math.Round(occ * 100.0 / act, 1) : 0;
                return new ProgressItem(
                    Label:          $"{khoa} ({occ}/{act})",
                    Value:          pct,
                    SecondaryValue: 90,
                    Color:          pct >= 90 ? "#ff4d4f" : pct >= 75 ? "#faad14" : "#52c41a");
            })
            .OrderByDescending(p => p.Value)
            .Take(15)
            .ToList();

        var progress = new ProgressListComponent(16, new ProgressListProps(
            Title:         "Công suất theo khoa (Top 15)",
            HeaderAction:  "Xem chi tiết",
            MaxValue:      100,
            Items:         items,
            FooterActions: null));

        // AlertList: khoa BOR >= 90 (critical) hoặc 75-89 (warning)
        var alerts = rows
            .Select(r =>
            {
                int occ = Int(r, "OccupiedBedCount");
                int act = Int(r, "ActualBedCount");
                double pct = act > 0 ? occ * 100.0 / act : 0;
                return (Row: r, Pct: pct);
            })
            .Where(x => x.Pct >= 75)
            .OrderByDescending(x => x.Pct)
            .Take(20)
            .Select(x => new AlertItem(
                Code:     Str(x.Row, "DepartmentCode") ?? "—",
                Text:     $"Công suất {Math.Round(x.Pct, 1)}% — cần kiểm tra",
                Patient:  $"{Int(x.Row, "OccupiedBedCount")}/{Int(x.Row, "ActualBedCount")} giường",
                Dept:     Str(x.Row, "KhoaDieuTri") ?? "",
                Time:     "hôm nay",
                Severity: x.Pct >= 90 ? "critical" : "warning"))
            .ToList();

        var alertList = new AlertListComponent(8, new AlertListProps(
            Title:         "Khoa quá tải",
            RealtimeBadge: true,
            MaxHeight:     400,
            TotalCount:    alerts.Count,
            Items:         alerts));

        return new SduiRow([progress, alertList]);
    }

    private static SduiRow BuildBottomRow(
        List<Dictionary<string, JsonElement>> rows,
        int occupied,
        int available,
        int disabled)
    {
        // FlowPipeline: trạng thái giường tổng
        var flow = new FlowPipelineComponent(12, new FlowPipelineProps(
            Title:  "Phân bổ giường theo trạng thái",
            Footer: $"Tổng: {occupied + available + disabled} giường",
            Stages: [
                new("Đang dùng",  occupied,  "#52c41a"),
                new("Còn trống",  available, "#1677ff"),
                new("Tắt/bảo trì", disabled,  "#8c8c8c"),
            ]));

        // ChartPie: tỷ lệ Occupied / Available / Disabled
        int total = occupied + available + disabled;
        var pieData = new List<ChartPieData>();
        if (occupied  > 0) pieData.Add(new("Đang dùng",  occupied));
        if (available > 0) pieData.Add(new("Còn trống",  available));
        if (disabled  > 0) pieData.Add(new("Tắt",        disabled));

        var pie = new ChartPieComponent(12, new ChartPieProps(
            Title:   "Tỷ lệ trạng thái giường",
            Height:  280,
            Variant: "donut",
            Legend:  true,
            Data:    pieData,
            Colors:  ["#52c41a", "#1677ff", "#8c8c8c"]));

        return new SduiRow([flow, pie]);
    }

    // ─────────────────────────────────────────────────────────
    // Helpers riêng — bed-occupancy lưu Date dạng ISO datetime,
    // cần parse linh hoạt hơn helper Date() ở SduiPageConfig.
    // ─────────────────────────────────────────────────────────

    private static DateOnly? DateOnlyOf(Dictionary<string, JsonElement> row, string key)
    {
        var s = Str(row, key);
        if (string.IsNullOrEmpty(s)) return null;
        if (DateOnly.TryParse(s, out var d))    return d;
        if (DateTime.TryParse(s, out var dt))   return DateOnly.FromDateTime(dt);
        return null;
    }

    private SduiPage BuildEmpty(DateOnly reportDate) =>
        new(
            Code:        Code,
            Title:       "Công suất giường bệnh",
            Badge:       "Trống",
            Live:        false,
            Subtitle:    $"Chưa có dữ liệu cho ngày {reportDate:dd/MM/yyyy}. Kiểm tra ViewBinding + chạy sync.",
            Actions:     [],
            Rows:        [],
            GeneratedAt: DateTime.UtcNow);
}
