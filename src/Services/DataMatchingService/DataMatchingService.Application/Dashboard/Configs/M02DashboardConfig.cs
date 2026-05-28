using System.Text.Json;

namespace Hdos.DataMatchingService.Application.Dashboard.Configs;

public sealed class M02DashboardConfig : DashboardConfig
{
    public override string Code  => "m02";
    public override string Title => "Trực quan Nội trú";

    public override IReadOnlyList<string> RecordTypes =>
        ["benh-nhan-noi-tru", "cau-hinh-giuong"];

    public override List<DashboardSection> BuildSections(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate)
    {
        var patients = data.GetValueOrDefault("benh-nhan-noi-tru", []);
        var bedCfg   = data.GetValueOrDefault("cau-hinh-giuong",   []);

        return
        [
            BuildKpiGrid(patients, bedCfg, reportDate),
            BuildDoiTuongKcb(patients),
            BuildTopIcd(patients),
            BuildBenhNhanTable(patients),
        ];
    }

    // --- Section builders ---

    private KpiGridSection BuildKpiGrid(
        List<Dictionary<string, JsonElement>> patients,
        List<Dictionary<string, JsonElement>> bedCfg,
        DateOnly reportDate)
    {
        int dangDieuTri   = patients.Count(r => Str(r, "TrangThai") == "DangNoiTru");
        int vaoVienHomNay = patients.Count(r => Date(r, "NgayNhap") == reportDate);
        int raVienHomNay  = patients.Count(r => Date(r, "NgayXuat")  == reportDate);

        double alos = dangDieuTri == 0 ? 0 : patients
            .Where(r => Str(r, "TrangThai") == "DangNoiTru")
            .Select(r =>
            {
                var nhap = Date(r, "NgayNhap");
                return nhap.HasValue
                    ? (reportDate.ToDateTime(TimeOnly.MinValue) - nhap.Value.ToDateTime(TimeOnly.MinValue)).TotalDays
                    : 0d;
            })
            .Average();

        int tongGiuong = bedCfg.Sum(r => Int(r, "TongGiuong"));
        double bor = tongGiuong > 0
            ? Math.Round(dangDieuTri * 100.0 / tongGiuong, 1)
            : 0;

        return new KpiGridSection("summary", "Tổng quan",
        [
            new("Tổng giường đang dùng", dangDieuTri, "giường",    "number"),
            new("Tổng giường khả dụng",  tongGiuong,  "giường",    "number"),
            new("BOR",                   bor,          "%",         "percent"),
            new("Đang điều trị",         dangDieuTri, "bệnh nhân", "number"),
            new("Vào viện hôm nay",      vaoVienHomNay,"lượt",     "number"),
            new("Ra viện hôm nay",       raVienHomNay, "lượt",     "number"),
            new("ALOS",                  Math.Round(alos, 1), "ngày/lượt", "days"),
        ]);
    }

    private static PieChartSection BuildDoiTuongKcb(List<Dictionary<string, JsonElement>> patients)
    {
        var groups = patients
            .GroupBy(r => Str(r, "DoiTuong") ?? "Khac")
            .Select(g => (Label: g.Key, Count: g.Count()))
            .ToList();

        int total = groups.Sum(g => g.Count);
        var slices = groups
            .OrderByDescending(g => g.Count)
            .Select(g => new PieSlice(
                g.Label,
                g.Count,
                total > 0 ? Math.Round(g.Count * 100.0 / total, 1) : 0))
            .ToList();

        return new PieChartSection("doi-tuong-kcb", "Phân loại đối tượng KCB", slices);
    }

    private static BarChartSection BuildTopIcd(List<Dictionary<string, JsonElement>> patients)
    {
        var items = patients
            .Where(r => !string.IsNullOrEmpty(Str(r, "MaICD")))
            .GroupBy(r => Str(r, "TenICD") ?? Str(r, "MaICD") ?? "")
            .Select(g => new BarItem(g.Key, g.Count()))
            .OrderByDescending(x => x.SoLuong)
            .Take(10)
            .ToList();

        return new BarChartSection("top-icd", "Top 10 ICD hôm nay", items);
    }

    private static TableSection BuildBenhNhanTable(List<Dictionary<string, JsonElement>> patients)
    {
        List<TableColumn> columns =
        [
            new("mrn",         "Bệnh nhân / MRN", "string"),
            new("tenKhoa",     "Khoa / Giường",   "string"),
            new("ngayNhap",    "Ngày nhập",        "date"),
            new("ngayXuat",    "Ngày xuất",        "date"),
            new("doiTuong",    "Đối tượng",        "badge"),
            new("trangThai",   "Trạng thái",       "badge"),
            new("chanDoan",    "Chẩn đoán",        "string"),
        ];

        var rows = patients
            .Where(r => Str(r, "TrangThai") == "DangNoiTru")
            .Select(r => new Dictionary<string, object?>
            {
                ["mrn"]       = Str(r, "MRN"),
                ["tenBenhNhan"] = Str(r, "TenBenhNhan"),
                ["soGiuong"]  = Str(r, "SoGiuong"),
                ["tenKhoa"]   = Str(r, "TenKhoa"),
                ["ngayNhap"]  = Str(r, "NgayNhap"),
                ["ngayXuat"]  = Str(r, "NgayXuat"),
                ["doiTuong"]  = Str(r, "DoiTuong"),
                ["trangThai"] = Str(r, "TrangThai"),
                ["chanDoan"]  = Str(r, "ChanDoan"),
            })
            .ToList();

        return new TableSection("danh-sach-benh-nhan", "Danh sách bệnh nhân nội trú", columns, rows);
    }
}
