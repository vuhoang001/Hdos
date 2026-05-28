using System.Text.Json;

namespace Hdos.DataMatchingService.Application.Sdui.Pages;

/// <summary>
/// Bảng điều hành bệnh viện — executive overview page.
/// GET /dm/pages/executive
/// </summary>
public sealed class ExecutiveSduiConfig : SduiPageConfig
{
    public override string Code => "executive";

    public override IReadOnlyList<string> RecordTypes =>
        ["benh-nhan-noi-tru", "cau-hinh-giuong"];

    public override SduiPage BuildPage(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate)
    {
        var patients = data.GetValueOrDefault("benh-nhan-noi-tru", []);
        var bedCfg   = data.GetValueOrDefault("cau-hinh-giuong",   []);

        int dangDieuTri   = patients.Count(r => Str(r, "TrangThai") == "DangNoiTru");
        int vaoVienHomNay = patients.Count(r => Date(r, "NgayNhap") == reportDate);
        int raVienHomNay  = patients.Count(r => Date(r, "NgayXuat")  == reportDate);
        int tongGiuong    = bedCfg.Sum(r => Int(r, "TongGiuong"));

        double bor = tongGiuong > 0
            ? Math.Round(dangDieuTri * 100.0 / tongGiuong, 1)
            : 0;

        return new SduiPage(
            Code:        Code,
            Title:       "Bảng điều hành bệnh viện",
            Badge:       "Trực tiếp",
            Live:        true,
            Subtitle:    $"Cập nhật: {DateTime.Now:HH:mm} · Ngày {reportDate:dd/MM/yyyy}",
            Actions:     [
                new("Xuất PDF", "default", null),
                new("Cài đặt", "default",  null),
            ],
            Rows:        [
                BuildKpiRow(dangDieuTri, bor, vaoVienHomNay, raVienHomNay),
                BuildMiddleRow(patients, bedCfg, tongGiuong),
                BuildBottomRow(patients),
            ],
            GeneratedAt: DateTime.UtcNow);
    }

    // --- Row builders ---

    private static SduiRow BuildKpiRow(int dangDieuTri, double bor, int vaoVien, int raVien) =>
        new([
            new KpiCardComponent(6, new KpiCardProps(
                Title: "Tổng bệnh nhân nội trú",
                Value: dangDieuTri,
                Accent: "#1677ff",
                Hint:   "đang điều trị",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title: "BOR",
                Value: $"{bor}%",
                Accent: bor >= 90 ? "#ff4d4f" : bor >= 75 ? "#faad14" : "#52c41a",
                Hint:   "công suất giường",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title: "Vào viện hôm nay",
                Value: vaoVien,
                Accent: "#52c41a",
                Hint:   "lượt nhập viện",
                HintColor: null)),

            new KpiCardComponent(6, new KpiCardProps(
                Title: "Ra viện hôm nay",
                Value: raVien,
                Accent: "#faad14",
                Hint:   "lượt xuất viện",
                HintColor: null)),
        ]);

    private static SduiRow BuildMiddleRow(
        List<Dictionary<string, JsonElement>> patients,
        List<Dictionary<string, JsonElement>> bedCfg,
        int tongGiuong)
    {
        // ProgressList: công suất giường theo khoa
        var progressItems = bedCfg
            .Select(bc =>
            {
                var khoa = bc.TryGetValue("TenKhoa", out var v) ? v.ToString() : "?";
                var max  = bc.TryGetValue("TongGiuong", out var mv)
                    && mv.ValueKind == JsonValueKind.Number && mv.TryGetInt32(out var m) ? m : 0;
                var used = patients.Count(p =>
                    p.TryGetValue("TenKhoa", out var pv) && pv.ToString() == khoa
                    && p.TryGetValue("TrangThai", out var ts) && ts.ToString() == "DangNoiTru");
                var pct  = max > 0 ? Math.Round(used * 100.0 / max, 1) : 0d;
                return new ProgressItem(
                    Label:          $"{khoa} ({used}/{max})",
                    Value:          pct,
                    SecondaryValue: 90,
                    Color: pct >= 90 ? "#ff4d4f" : pct >= 75 ? "#faad14" : "#52c41a");
            })
            .ToList();

        var progressList = new ProgressListComponent(8, new ProgressListProps(
            Title:         "Công suất giường theo khoa",
            HeaderAction:  "Xem chi tiết",
            MaxValue:      100,
            Items:         progressItems,
            FooterActions: null));

        // AlertList: bệnh nhân có cảnh báo (mock — thực tế lấy từ record type riêng)
        var alerts = patients
            .Where(r => Str(r, "TrangThai") == "DangNoiTru" && !string.IsNullOrEmpty(Str(r, "CanhBao")))
            .Take(20)
            .Select(r => new AlertItem(
                Code:     Str(r, "MaICD") ?? "N/A",
                Text:     Str(r, "CanhBao") ?? "",
                Patient:  Str(r, "TenBenhNhan") ?? Str(r, "MRN") ?? "",
                Dept:     Str(r, "TenKhoa") ?? "",
                Time:     "vừa xong",
                Severity: "warning"))
            .ToList();

        var alertList = new AlertListComponent(16, new AlertListProps(
            Title:         "Cảnh báo lâm sàng",
            RealtimeBadge: true,
            MaxHeight:     400,
            TotalCount:    alerts.Count,
            Items:         alerts));

        return new SduiRow([progressList, alertList]);
    }

    private static SduiRow BuildBottomRow(List<Dictionary<string, JsonElement>> patients)
    {
        // FlowPipeline: dòng bệnh nhân qua các giai đoạn
        int choKhamSang = patients.Count(r => Str(r, "TrangThai") == "ChoKhamSang");
        int dangNoiTru  = patients.Count(r => Str(r, "TrangThai") == "DangNoiTru");
        int choXuatVien = patients.Count(r => Str(r, "TrangThai") == "ChoXuatVien");
        int daXuatVien  = patients.Count(r => Str(r, "TrangThai") == "DaXuatVien");

        var flow = new FlowPipelineComponent(12, new FlowPipelineProps(
            Title:  "Dòng bệnh nhân",
            Footer: $"Tổng: {patients.Count} lượt",
            Stages: [
                new("Chờ khám sàng", choKhamSang, "#1677ff"),
                new("Đang nội trú",  dangNoiTru,  "#52c41a"),
                new("Chờ xuất viện", choXuatVien, "#faad14"),
                new("Đã xuất viện",  daXuatVien,  "#8c8c8c"),
            ]));

        // ChartPie: phân loại đối tượng KCB
        var groups = patients
            .GroupBy(r => Str(r, "DoiTuong") ?? "Khác")
            .Select(g => (Label: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ToList();

        int total = groups.Sum(g => g.Count);
        var pieData = groups
            .Select(g => new ChartPieData(g.Label, g.Count))
            .ToList();

        var pie = new ChartPieComponent(12, new ChartPieProps(
            Title:   "Đối tượng KCB",
            Height:  260,
            Variant: "donut",
            Legend:  true,
            Data:    pieData,
            Colors:  ["#1677ff", "#52c41a", "#faad14", "#ff4d4f", "#722ed1"]));

        return new SduiRow([flow, pie]);
    }
}
