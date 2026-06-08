using System.Text.Json;

namespace Hdos.DataMatchingService.Application.Sdui.Pages;

/// <summary>
/// Dashboard tài chính theo ngày — đọc <c>finance-daily</c> RecordType từ StagingRecord
/// (data đã ingest qua MVP B hoặc REST). User tự đăng ký SourceProfile mapping
/// view columns → canonical fields dưới đây.
///
/// GET /dm/pages/finance-daily?date=yyyy-MM-dd
///
/// Canonical fields chart đọc:
///   Date         (timestamp/date string)
///   TenKhoa      (string, bắt buộc)
///   MaKhoa       (string, optional)
///   DoanhThu     (number, bắt buộc)
///   ChiPhi       (number, optional — default 0)
///   LoiNhuan     (number, optional — computed = DoanhThu - ChiPhi nếu thiếu)
///   SoBenhNhan   (int, optional)
/// </summary>
public sealed class FinanceDailySduiConfig : SduiPageConfig
{
    public override string Code => "finance-daily";

    public override IReadOnlyList<string> RecordTypes => ["finance-daily"];

    public override SduiPage BuildPage(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate)
    {
        var rows = data.GetValueOrDefault("finance-daily", []);

        if (rows.Count == 0)
            return BuildEmpty(reportDate);

        // Filter theo ngày báo cáo, fallback toàn bộ nếu không khớp
        var todayRows = rows.Where(r => DateOnlyOf(r, "Date") == reportDate).ToList();
        var effective = todayRows.Count > 0 ? todayRows : rows;

        decimal totalRevenue = effective.Sum(r => Dec(r, "DoanhThu"));
        decimal totalCost    = effective.Sum(r => Dec(r, "ChiPhi"));
        decimal totalProfit  = effective.Sum(r => DecOrComputed(r, "LoiNhuan", "DoanhThu", "ChiPhi"));
        int     totalPatient = effective.Sum(r => Int(r, "SoBenhNhan"));

        return new SduiPage(
            Code:        Code,
            Title:       "Tài chính theo ngày",
            Badge:       todayRows.Count > 0 ? "Đúng ngày" : "Mới nhất",
            Live:        true,
            Subtitle:    $"Cập nhật {DateTime.UtcNow.AddHours(7):HH:mm} · Ngày {reportDate:dd/MM/yyyy} · {effective.Count} khoa",
            Actions:     [
                new("Xuất Excel", "default", null),
                new("Cài đặt",   "default", null),
            ],
            Rows:        [
                BuildKpiRow(totalRevenue, totalCost, totalProfit, totalPatient),
                BuildProgressAndAlertRow(effective),
                BuildFlowAndPieRow(effective, totalRevenue, totalCost, totalProfit),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    // ─────────────────────────────────────────────────────────
    // Row builders
    // ─────────────────────────────────────────────────────────

    private static SduiRow BuildKpiRow(decimal revenue, decimal cost, decimal profit, int patients)
    {
        // Format VNĐ ngắn gọn
        string FormatVnd(decimal v) =>
            Math.Abs(v) >= 1_000_000_000m ? $"{v / 1_000_000_000m:0.##} tỷ"
            : Math.Abs(v) >= 1_000_000m   ? $"{v / 1_000_000m:0.##} tr"
            : $"{v:N0} đ";

        decimal margin = revenue > 0 ? Math.Round(profit * 100m / revenue, 1) : 0;

        return new SduiRow([
            new KpiCardComponent(6, new KpiCardProps(
                Title:     "Tổng doanh thu",
                Value:     FormatVnd(revenue),
                Accent:    "#1677ff",
                Hint:      "VNĐ",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:     "Tổng chi phí",
                Value:     FormatVnd(cost),
                Accent:    "#faad14",
                Hint:      "VNĐ",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:     "Lợi nhuận",
                Value:     FormatVnd(profit),
                Accent:    profit < 0 ? "#ff4d4f" : profit < revenue * 0.1m ? "#faad14" : "#52c41a",
                Hint:      revenue > 0 ? $"Margin: {margin}%" : "—",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:     "Số bệnh nhân",
                Value:     patients,
                Accent:    "#722ed1",
                Hint:      "lượt",
                HintColor: null)),
        ]);
    }

    private static SduiRow BuildProgressAndAlertRow(List<Dictionary<string, JsonElement>> rows)
    {
        // ProgressList: Top 15 khoa theo doanh thu
        var byDept = rows
            .GroupBy(r => Str(r, "TenKhoa") ?? "(không tên)")
            .Select(g => new
            {
                Khoa     = g.Key,
                Revenue  = g.Sum(r => Dec(r, "DoanhThu")),
                Cost     = g.Sum(r => Dec(r, "ChiPhi")),
                Profit   = g.Sum(r => DecOrComputed(r, "LoiNhuan", "DoanhThu", "ChiPhi")),
                MaKhoa   = Str(g.First(), "MaKhoa") ?? "—",
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        decimal maxRevenue = byDept.Count > 0 ? byDept.Max(x => x.Revenue) : 1;

        var items = byDept
            .Take(15)
            .Select(x =>
            {
                double pct = maxRevenue > 0 ? Math.Round((double)(x.Revenue * 100m / maxRevenue), 1) : 0;
                return new ProgressItem(
                    Label:          $"{x.Khoa} ({FormatShort(x.Revenue)})",
                    Value:          pct,
                    SecondaryValue: null,
                    Color:          x.Profit < 0      ? "#ff4d4f"
                                  : x.Profit / Math.Max(x.Revenue, 1) < 0.1m ? "#faad14"
                                  :                     "#52c41a");
            })
            .ToList();

        var progress = new ProgressListComponent(16, new ProgressListProps(
            Title:         "Top 15 khoa theo doanh thu",
            HeaderAction:  null,
            MaxValue:      100,
            Items:         items,
            FooterActions: null));

        // AlertList: khoa lỗ (LoiNhuan < 0)
        var alerts = byDept
            .Where(x => x.Profit < 0)
            .OrderBy(x => x.Profit)  // âm nhất trước
            .Take(20)
            .Select(x => new AlertItem(
                Code:     x.MaKhoa,
                Text:     $"Lỗ {FormatShort(Math.Abs(x.Profit))} (DT {FormatShort(x.Revenue)}, CP {FormatShort(x.Cost)})",
                Patient:  $"DT/CP = {(x.Cost > 0 ? Math.Round((double)(x.Revenue * 100m / x.Cost), 1) : 0)}%",
                Dept:     x.Khoa,
                Time:     "hôm nay",
                Severity: x.Profit < -10_000_000m ? "critical" : "warning"))
            .ToList();

        var alertList = new AlertListComponent(8, new AlertListProps(
            Title:         "Khoa đang lỗ",
            RealtimeBadge: true,
            MaxHeight:     400,
            TotalCount:    alerts.Count,
            Items:         alerts));

        return new SduiRow([progress, alertList]);
    }

    private static SduiRow BuildFlowAndPieRow(
        List<Dictionary<string, JsonElement>> rows,
        decimal totalRevenue,
        decimal totalCost,
        decimal totalProfit)
    {
        // FlowPipeline: Doanh thu → Chi phí → Lợi nhuận
        var flow = new FlowPipelineComponent(12, new FlowPipelineProps(
            Title:  "Dòng tài chính",
            Footer: $"Tỉ suất lợi nhuận: {(totalRevenue > 0 ? Math.Round(totalProfit * 100m / totalRevenue, 1) : 0)}%",
            Stages: [
                new("Doanh thu", (int)(totalRevenue / 1_000_000m), "#1677ff"),
                new("Chi phí",   (int)(totalCost    / 1_000_000m), "#faad14"),
                new("Lợi nhuận", (int)(totalProfit  / 1_000_000m), totalProfit < 0 ? "#ff4d4f" : "#52c41a"),
            ]));

        // ChartPie: phân bổ doanh thu (top 8 khoa + "Khác")
        var byDept = rows
            .GroupBy(r => Str(r, "TenKhoa") ?? "(không tên)")
            .Select(g => (Khoa: g.Key, Revenue: g.Sum(r => Dec(r, "DoanhThu"))))
            .Where(x => x.Revenue > 0)
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var pieData = new List<ChartPieData>();
        pieData.AddRange(byDept.Take(8).Select(x => new ChartPieData(x.Khoa, (double)x.Revenue)));
        if (byDept.Count > 8)
        {
            var khac = byDept.Skip(8).Sum(x => x.Revenue);
            if (khac > 0) pieData.Add(new ChartPieData("Khác", (double)khac));
        }

        var pie = new ChartPieComponent(12, new ChartPieProps(
            Title:   "Phân bổ doanh thu theo khoa",
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

    /// <summary>Format VNĐ ngắn: 1500000000 → "1.5 tỷ", 25000 → "25 000 đ"</summary>
    private static string FormatShort(decimal v) =>
        Math.Abs(v) >= 1_000_000_000m ? $"{v / 1_000_000_000m:0.#}T"
        : Math.Abs(v) >= 1_000_000m   ? $"{v / 1_000_000m:0.#}tr"
        : Math.Abs(v) >= 1_000m       ? $"{v / 1_000m:0}k"
        : $"{v:N0}";

    /// <summary>DateOnly fallback DateTime — match canonical Date có thể là ISO datetime.</summary>
    private static DateOnly? DateOnlyOf(Dictionary<string, JsonElement> row, string key)
    {
        var s = Str(row, key);
        if (string.IsNullOrEmpty(s)) return null;
        if (DateOnly.TryParse(s, out var d))    return d;
        if (DateTime.TryParse(s, out var dt))   return DateOnly.FromDateTime(dt);
        return null;
    }

    /// <summary>Lấy LoiNhuan trực tiếp, nếu thiếu thì compute = DoanhThu - ChiPhi.</summary>
    private static decimal DecOrComputed(
        Dictionary<string, JsonElement> row, string targetKey, string aKey, string bKey)
    {
        if (row.TryGetValue(targetKey, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetDecimal(out var d))
            return d;
        return Dec(row, aKey) - Dec(row, bKey);
    }

    private SduiPage BuildEmpty(DateOnly reportDate) =>
        new(
            Code, "Tài chính theo ngày", "Trống", false,
            $"Chưa có dữ liệu cho ngày {reportDate:dd/MM/yyyy}. Kiểm tra SourceProfile + ingest data.",
            [], [], DateTime.UtcNow);
}
