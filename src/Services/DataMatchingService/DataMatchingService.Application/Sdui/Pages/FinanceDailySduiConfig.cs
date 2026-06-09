using System.Text.Json;

namespace Hdos.DataMatchingService.Application.Sdui.Pages;

/// <summary>
/// Dashboard tài chính theo ngày — đọc <c>finance-daily</c> RecordType từ StagingRecord
/// đã ingest từ view <c>api.finance_daily</c> qua <c>with-auto-profile</c>.
///
/// GET /dm/pages/finance-daily?date=yyyy-MM-dd
///
/// Canonical fields thực tế (auto-suggest từ snake_case → PascalCase):
///   Date                    (ISO datetime string)
///   DepartmentId            (int — view không có department name)
///   RoomId                  (int)
///   FinanceBucket           (string — loại hóa đơn, vd "invoice_type_3")
///   InvoiceTypeId / InvoiceFormId / InvoiceTypeDetailId
///   PaymentGroupId / PaymentSourceId
///   InvoiceCount            (int)
///   DistinctEncounterCount  (int — số lượt khám phân biệt)
///   TotalInvoiceAmount      (decimal — doanh thu hóa đơn)
///   TotalDiscountAmount     (decimal — giảm giá)
/// </summary>
#pragma warning disable CS0618 // Soft deprecated (doc 53 P6) — chuyển sang DataContract khi tới P7
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

        var todayRows = rows.Where(r => DateOnlyOf(r, "Date") == reportDate).ToList();
        var effective = todayRows.Count > 0 ? todayRows : rows;

        decimal totalInvoice  = effective.Sum(r => Dec(r, "TotalInvoiceAmount"));
        decimal totalDiscount = effective.Sum(r => Dec(r, "TotalDiscountAmount"));
        decimal netRevenue    = totalInvoice - totalDiscount;
        int     totalInvoices = effective.Sum(r => Int(r, "InvoiceCount"));
        int     totalEncs     = effective.Sum(r => Int(r, "DistinctEncounterCount"));

        return new SduiPage(
            Code:        Code,
            Title:       "Tài chính theo ngày",
            Badge:       todayRows.Count > 0 ? "Đúng ngày" : "Mới nhất",
            Live:        true,
            Subtitle:    $"Cập nhật {DateTime.UtcNow.AddHours(7):HH:mm} · Ngày {reportDate:dd/MM/yyyy} · {effective.Count} dòng",
            Actions:     [
                new("Xuất Excel", "default", null),
                new("Cài đặt",   "default", null),
            ],
            Rows:        [
                BuildKpiRow(totalInvoice, totalDiscount, totalInvoices, totalEncs),
                BuildProgressAndAlertRow(effective),
                BuildFlowAndPieRow(effective, totalInvoice, totalDiscount, netRevenue),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    // ─────────────────────────────────────────────────────────
    // Row builders
    // ─────────────────────────────────────────────────────────

    private static SduiRow BuildKpiRow(decimal invoice, decimal discount, int invoiceCount, int encounters)
    {
        decimal net          = invoice - discount;
        decimal discountRate = invoice > 0 ? Math.Round(discount * 100m / invoice, 1) : 0;

        return new SduiRow([
            new KpiCardComponent(6, new KpiCardProps(
                Title:     "Tổng doanh thu",
                Value:     FormatVnd(invoice),
                Accent:    "#1677ff",
                Hint:      "VNĐ hóa đơn",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:     "Tổng giảm giá",
                Value:     FormatVnd(discount),
                Accent:    discountRate >= 30 ? "#ff4d4f" : discountRate >= 15 ? "#faad14" : "#52c41a",
                Hint:      $"{discountRate}% doanh thu",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:     "Số hóa đơn",
                Value:     invoiceCount,
                Accent:    "#722ed1",
                Hint:      $"DT thực: {FormatVnd(net)}",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title:     "Lượt khám",
                Value:     encounters,
                Accent:    "#13c2c2",
                Hint:      encounters > 0 ? $"AVG: {FormatVnd(invoice / encounters)}/lượt" : "—",
                HintColor: null)),
        ]);
    }

    private static SduiRow BuildProgressAndAlertRow(List<Dictionary<string, JsonElement>> rows)
    {
        // ProgressList: Top 15 khoa theo doanh thu (view chỉ có DepartmentId → "Khoa #{id}")
        var byDept = rows
            .GroupBy(r => Int(r, "DepartmentId"))
            .Select(g => new
            {
                DeptId   = g.Key,
                Invoice  = g.Sum(r => Dec(r, "TotalInvoiceAmount")),
                Discount = g.Sum(r => Dec(r, "TotalDiscountAmount")),
                Encs     = g.Sum(r => Int(r, "DistinctEncounterCount")),
            })
            .OrderByDescending(x => x.Invoice)
            .ToList();

        decimal maxInvoice = byDept.Count > 0 ? byDept.Max(x => x.Invoice) : 1;

        var items = byDept
            .Take(15)
            .Select(x =>
            {
                double pct          = maxInvoice > 0 ? Math.Round((double)(x.Invoice * 100m / maxInvoice), 1) : 0;
                decimal discRate    = x.Invoice > 0 ? x.Discount * 100m / x.Invoice : 0;
                return new ProgressItem(
                    Label:          $"Khoa #{x.DeptId} ({FormatShort(x.Invoice)})",
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

        // AlertList: khoa giảm giá ≥ 30% doanh thu
        var alerts = byDept
            .Where(x => x.Invoice > 0)
            .Select(x => (x.DeptId, x.Invoice, x.Discount, Pct: x.Discount * 100m / x.Invoice))
            .Where(x => x.Pct >= 30)
            .OrderByDescending(x => x.Pct)
            .Take(20)
            .Select(x => new AlertItem(
                Code:     $"K#{x.DeptId}",
                Text:     $"Giảm {Math.Round(x.Pct, 1)}% — {FormatShort(x.Discount)} / {FormatShort(x.Invoice)}",
                Patient:  "—",
                Dept:     $"Khoa #{x.DeptId}",
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

    private static SduiRow BuildFlowAndPieRow(
        List<Dictionary<string, JsonElement>> rows,
        decimal totalInvoice,
        decimal totalDiscount,
        decimal netRevenue)
    {
        // FlowPipeline: Doanh thu → Giảm giá → Doanh thu thực (đơn vị triệu để hiển thị)
        var flow = new FlowPipelineComponent(12, new FlowPipelineProps(
            Title:  "Dòng doanh thu",
            Footer: $"Tỉ lệ giảm: {(totalInvoice > 0 ? Math.Round(totalDiscount * 100m / totalInvoice, 1) : 0)}%",
            Stages: [
                new("Doanh thu",    (int)(totalInvoice  / 1_000_000m), "#1677ff"),
                new("Giảm giá",     (int)(totalDiscount / 1_000_000m), "#faad14"),
                new("DT thực",      (int)(netRevenue    / 1_000_000m), netRevenue < 0 ? "#ff4d4f" : "#52c41a"),
            ]));

        // ChartPie: phân bổ doanh thu theo FinanceBucket (loại hóa đơn)
        var byBucket = rows
            .GroupBy(r => Str(r, "FinanceBucket") ?? "(không có)")
            .Select(g => (Bucket: g.Key, Revenue: g.Sum(r => Dec(r, "TotalInvoiceAmount"))))
            .Where(x => x.Revenue > 0)
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var pieData = new List<ChartPieData>();
        pieData.AddRange(byBucket.Take(8).Select(x => new ChartPieData(x.Bucket, (double)x.Revenue)));
        if (byBucket.Count > 8)
        {
            var khac = byBucket.Skip(8).Sum(x => x.Revenue);
            if (khac > 0) pieData.Add(new ChartPieData("Khác", (double)khac));
        }

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
            Code, "Tài chính theo ngày", "Trống", false,
            $"Chưa có dữ liệu cho ngày {reportDate:dd/MM/yyyy}. Kiểm tra SourceProfile + ingest.",
            [], [], DateTime.UtcNow);
}
