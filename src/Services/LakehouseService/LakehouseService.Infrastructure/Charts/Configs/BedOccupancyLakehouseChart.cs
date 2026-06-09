using Hdos.LakehouseService.Application.Charts.Sdui;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.Charts.Configs;

/// <summary>
/// Chart công suất giường — query trực tiếp warehouse view <c>api.bed_occupancy</c>
/// bằng raw SQL (Npgsql), không qua StagingRecord / SourceProfile mapping.
///
/// GET /lakehouse/charts/bed-occupancy?date=yyyy-MM-dd&amp;department=ICU
///
/// Filters động:
///   date       — ngày báo cáo (mặc định hôm nay UTC)
///   department — ILIKE match department_name hoặc department_code
/// </summary>
#pragma warning disable CS0618 // Soft deprecated (doc 53 P6) — chuyển sang DataContract khi tới P7
public sealed class BedOccupancyLakehouseChart : ILakehouseChartConfig
{
    public string Code => "bed-occupancy";

    private const string ViewName = "api.bed_occupancy";

    public async Task<SduiPage> BuildAsync(
        NpgsqlDataSource  ds,
        DateOnly          reportDate,
        IQueryCollection  query,
        CancellationToken ct)
    {
        // Demo mode — trả SduiPage hardcoded để verify shape không cần warehouse DB.
        // FE / DynamicForm có thể mock-test mapping mà không phụ thuộc data thật.
        if (string.Equals(query["demo"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase))
            return BuildDemo(reportDate);

        var dept = query["department"].FirstOrDefault();

        // ── [1] Aggregate tổng — 1 query gom tất cả KPI ────────────────────
        var totals = await FetchTotalsAsync(ds, reportDate, dept, ct);

        if (totals.Actual == 0 && totals.Planned == 0)
            return BuildEmpty(reportDate, dept);

        // ── [2] Detail theo khoa — query khác, server-side GROUP BY ────────
        var perDept = await FetchPerDepartmentAsync(ds, reportDate, dept, ct);

        return new SduiPage(
            Code:        Code,
            Title:       "Công suất giường bệnh (Live từ Lakehouse)",
            Badge:       "Live",
            Live:        true,
            Subtitle:    $"Lakehouse trực tiếp · {DateTime.UtcNow.AddHours(7):HH:mm} · Ngày {reportDate:dd/MM/yyyy}"
                       + (dept is null ? "" : $" · khoa {dept}"),
            Actions:     [new("Xuất Excel", "default", null)],
            Rows:        [
                BuildKpiRow(totals),
                BuildProgressAndAlertRow(perDept),
                BuildFlowAndPieRow(totals),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    // ─────────────────────────────────────────────────────────────────────
    // SQL queries — raw Npgsql, không EF, không entity mapping
    // ─────────────────────────────────────────────────────────────────────

    private sealed record Totals(int Planned, int Actual, int Occupied, int Available, int Disabled);
    private sealed record PerDept(string Khoa, string Code, int Occupied, int Actual);

    private static async Task<Totals> FetchTotalsAsync(
        NpgsqlDataSource ds, DateOnly date, string? deptFilter, CancellationToken ct)
    {
        const string sql = $"""
            SELECT
                COALESCE(SUM(planned_bed_count),   0)::int AS planned,
                COALESCE(SUM(actual_bed_count),    0)::int AS actual,
                COALESCE(SUM(occupied_bed_count),  0)::int AS occupied,
                COALESCE(SUM(available_bed_count), 0)::int AS available,
                COALESCE(SUM(disabled_bed_count),  0)::int AS disabled
            FROM {ViewName}
            WHERE date = @d
              AND (@dept IS NULL
                   OR department_name ILIKE '%' || @dept || '%'
                   OR department_code ILIKE @dept)
        """;

        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@d",    date);
        cmd.Parameters.AddWithValue("@dept", (object?)deptFilter ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new Totals(0, 0, 0, 0, 0);

        return new Totals(
            Planned:   reader.GetInt32(0),
            Actual:    reader.GetInt32(1),
            Occupied:  reader.GetInt32(2),
            Available: reader.GetInt32(3),
            Disabled:  reader.GetInt32(4));
    }

    private static async Task<List<PerDept>> FetchPerDepartmentAsync(
        NpgsqlDataSource ds, DateOnly date, string? deptFilter, CancellationToken ct)
    {
        const string sql = $"""
            SELECT
                COALESCE(department_name, '(không tên)')           AS khoa,
                COALESCE(department_code, '—')                     AS code,
                COALESCE(SUM(occupied_bed_count), 0)::int           AS occupied,
                COALESCE(SUM(actual_bed_count),   0)::int           AS actual
            FROM {ViewName}
            WHERE date = @d
              AND (@dept IS NULL
                   OR department_name ILIKE '%' || @dept || '%'
                   OR department_code ILIKE @dept)
            GROUP BY department_name, department_code
            ORDER BY SUM(occupied_bed_count) DESC NULLS LAST
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
                Khoa:     reader.GetString(0),
                Code:     reader.GetString(1),
                Occupied: reader.GetInt32(2),
                Actual:   reader.GetInt32(3)));
        return list;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Section builders
    // ─────────────────────────────────────────────────────────────────────

    private static SduiRow BuildKpiRow(Totals t)
    {
        double bor = t.Actual > 0 ? Math.Round(t.Occupied * 100.0 / t.Actual, 1) : 0;

        return new SduiRow([
            new KpiCardComponent(6, new KpiCardProps(
                Title: "Tổng giường",       Value: t.Actual,
                Accent: "#1677ff",
                Hint: $"Kế hoạch: {t.Planned}", HintColor: null)),
            new KpiCardComponent(6, new KpiCardProps(
                Title: "Đang sử dụng",      Value: t.Occupied,
                Accent: "#52c41a", Hint: "giường", HintColor: null)),
            new KpiCardComponent(6, new KpiCardProps(
                Title: "Còn trống",          Value: t.Available,
                Accent: "#faad14", Hint: "khả dụng", HintColor: null)),
            new KpiCardComponent(6, new KpiCardProps(
                Title: "BOR",                Value: $"{bor}%",
                Accent: bor >= 90 ? "#ff4d4f" : bor >= 75 ? "#faad14" : "#52c41a",
                Hint: "công suất", HintColor: null)),
        ]);
    }

    private static SduiRow BuildProgressAndAlertRow(List<PerDept> rows)
    {
        var items = rows
            .Take(15)
            .Select(r =>
            {
                double pct = r.Actual > 0 ? Math.Round(r.Occupied * 100.0 / r.Actual, 1) : 0;
                return new ProgressItem(
                    Label:          $"{r.Khoa} ({r.Occupied}/{r.Actual})",
                    Value:          pct,
                    SecondaryValue: 90,
                    Color:          pct >= 90 ? "#ff4d4f" : pct >= 75 ? "#faad14" : "#52c41a");
            })
            .ToList();

        var progress = new ProgressListComponent(16, new ProgressListProps(
            Title:         "Công suất theo khoa (Top 15)",
            HeaderAction:  null,
            MaxValue:      100,
            Items:         items,
            FooterActions: null));

        var alerts = rows
            .Select(r => (Row: r, Pct: r.Actual > 0 ? r.Occupied * 100.0 / r.Actual : 0))
            .Where(x => x.Pct >= 75)
            .OrderByDescending(x => x.Pct)
            .Take(20)
            .Select(x => new AlertItem(
                Code:     x.Row.Code,
                Text:     $"Công suất {Math.Round(x.Pct, 1)}% — cần kiểm tra",
                Patient:  $"{x.Row.Occupied}/{x.Row.Actual} giường",
                Dept:     x.Row.Khoa,
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

    private static SduiRow BuildFlowAndPieRow(Totals t)
    {
        var flow = new FlowPipelineComponent(12, new FlowPipelineProps(
            Title:  "Phân bổ trạng thái",
            Footer: $"Tổng: {t.Occupied + t.Available + t.Disabled} giường",
            Stages: [
                new("Đang dùng",   t.Occupied,  "#52c41a"),
                new("Còn trống",   t.Available, "#1677ff"),
                new("Tắt/bảo trì", t.Disabled,  "#8c8c8c"),
            ]));

        var pieData = new List<ChartPieData>();
        if (t.Occupied  > 0) pieData.Add(new("Đang dùng", t.Occupied));
        if (t.Available > 0) pieData.Add(new("Còn trống", t.Available));
        if (t.Disabled  > 0) pieData.Add(new("Tắt",        t.Disabled));

        var pie = new ChartPieComponent(12, new ChartPieProps(
            Title:   "Tỷ lệ trạng thái giường",
            Height:  280,
            Variant: "donut",
            Legend:  true,
            Data:    pieData,
            Colors:  ["#52c41a", "#1677ff", "#8c8c8c"]));

        return new SduiRow([flow, pie]);
    }

    private SduiPage BuildEmpty(DateOnly reportDate, string? dept) =>
        new(
            Code, "Công suất giường bệnh (Live)", "Trống", false,
            $"Không có dữ liệu ngày {reportDate:dd/MM/yyyy}"
            + (dept is null ? "" : $" cho khoa '{dept}'") + ".",
            [], [], DateTime.UtcNow);

    // ─────────────────────────────────────────────────────────────────────
    // Demo mode — fake SduiPage, không query warehouse. Cho mục đích
    // verify mapping shape với DynamicForm / FE renderer.
    // ─────────────────────────────────────────────────────────────────────
    private SduiPage BuildDemo(DateOnly reportDate)
    {
        // Giả lập 4 khoa
        var fakeDepts = new List<PerDept>
        {
            new("Khoa Hồi sức tích cực nhi", "K48",  17, 18),  // BOR 94.4% — critical
            new("Khoa Cấp cứu",               "K-CC", 8,  10),  // BOR 80%   — warning
            new("Khoa Nội tiết",              "K-NT", 12, 25),  // BOR 48%   — green
            new("Khoa Sơ sinh",               "K-SS", 30, 98),  // BOR 30.6% — green
        };

        var totals = new Totals(
            Planned:   75,
            Actual:    151,
            Occupied:  67,
            Available: 84,
            Disabled:  0);

        return new SduiPage(
            Code:        Code,
            Title:       "Công suất giường bệnh (Demo)",
            Badge:       "Demo",
            Live:        false,
            Subtitle:    $"⚠ DEMO MODE — fake data, không từ warehouse · Ngày {reportDate:dd/MM/yyyy}",
            Actions:     [new("Xuất Excel", "default", null)],
            Rows:        [
                BuildKpiRow(totals),
                BuildProgressAndAlertRow(fakeDepts),
                BuildFlowAndPieRow(totals),
            ],
            GeneratedAt: DateTime.UtcNow);
    }
}
