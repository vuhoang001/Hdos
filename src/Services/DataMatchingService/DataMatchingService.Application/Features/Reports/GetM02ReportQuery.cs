using System.Text.Json;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.Reports;

// GET /dm/reports/m02?sourceSystem=his-01&date=2026-05-28
// Đọc StagingRecord với RecordType = "benh-nhan-noi-tru" từ HIS đã ingest,
// bổ sung sức chứa giường từ RecordType = "cau-hinh-giuong" nếu có.
public sealed record GetM02ReportQuery(
    string? SourceSystem,
    DateOnly? Date) : IRequest<Result<M02ReportDto>>;

public sealed class GetM02ReportHandler(IStagingRecordRepository records)
    : IRequestHandler<GetM02ReportQuery, Result<M02ReportDto>>
{
    public async Task<Result<M02ReportDto>> Handle(GetM02ReportQuery request, CancellationToken ct)
    {
        var reportDate = request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var (rows, bedConfigRows) = await FetchSourceDataAsync(request.SourceSystem, ct);

        var summary  = BuildSummary(rows, bedConfigRows, reportDate);
        var doiTuong = BuildDoiTuongKcb(rows);
        var topIcd   = BuildTopIcd(rows);
        var danhSach = BuildDanhSachNoiTru(rows);

        return Result.Success(new M02ReportDto(reportDate, DateTime.UtcNow, summary, doiTuong, topIcd, danhSach));
    }

    private async Task<(List<Dictionary<string, JsonElement>> patients, List<Dictionary<string, JsonElement>> bedCfg)>
        FetchSourceDataAsync(string? sourceSystem, CancellationToken ct)
    {
        var patientTask   = records.GetMatchedAsync(sourceSystem, "benh-nhan-noi-tru", null, null, ct);
        var bedConfigTask = records.GetMatchedAsync(sourceSystem, "cau-hinh-giuong",   null, null, ct);
        await Task.WhenAll(patientTask, bedConfigTask);

        return (ParsePayloads(patientTask.Result), ParsePayloads(bedConfigTask.Result));
    }

    private static List<Dictionary<string, JsonElement>> ParsePayloads(
        IEnumerable<Domain.Entities.StagingRecord> source) =>
        source
            .Where(r => !string.IsNullOrEmpty(r.CanonicalPayload))
            .Select(r =>
            {
                try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(r.CanonicalPayload!) ?? []; }
                catch { return new Dictionary<string, JsonElement>(); }
            })
            .Where(d => d.Count > 0)
            .ToList();

    private static M02SummaryDto BuildSummary(
        List<Dictionary<string, JsonElement>> rows,
        List<Dictionary<string, JsonElement>> bedCfg,
        DateOnly reportDate)
    {
        int dangDieuTri   = rows.Count(r => GetString(r, "TrangThai") == "DangNoiTru");
        int vaoVienHomNay = rows.Count(r => ParseDate(GetString(r, "NgayNhap")) == reportDate);
        int raVienHomNay  = rows.Count(r => ParseDate(GetString(r, "NgayXuat"))  == reportDate);

        double alos = dangDieuTri == 0 ? 0 : rows
            .Where(r => GetString(r, "TrangThai") == "DangNoiTru")
            .Select(r =>
            {
                var nhap = ParseDate(GetString(r, "NgayNhap"));
                return nhap.HasValue
                    ? (reportDate.ToDateTime(TimeOnly.MinValue) - nhap.Value.ToDateTime(TimeOnly.MinValue)).TotalDays
                    : 0d;
            })
            .Average();

        int tongGiuongKhaDung = bedCfg.Sum(r => GetInt(r, "TongGiuong"));
        double bor = tongGiuongKhaDung > 0
            ? Math.Round(dangDieuTri * 100.0 / tongGiuongKhaDung, 1)
            : 0;

        return new M02SummaryDto(
            TongGiuongDangSuDung: dangDieuTri,
            TongGiuongKhaDung:    tongGiuongKhaDung,
            BorPercent:           bor,
            DangDieuTri:          dangDieuTri,
            VaoVienHomNay:        vaoVienHomNay,
            RaVienHomNay:         raVienHomNay,
            Alos:                 Math.Round(alos, 1));
    }

    private static List<M02DoiTuongDto> BuildDoiTuongKcb(List<Dictionary<string, JsonElement>> rows)
    {
        var groups = rows
            .GroupBy(r => GetString(r, "DoiTuong") ?? "Khac")
            .Select(g => (DoiTuong: g.Key, Count: g.Count()))
            .ToList();

        int total = groups.Sum(g => g.Count);
        return groups
            .OrderByDescending(g => g.Count)
            .Select(g => new M02DoiTuongDto(
                g.DoiTuong,
                g.Count,
                total > 0 ? Math.Round(g.Count * 100.0 / total, 1) : 0))
            .ToList();
    }

    private static List<M02IcdDto> BuildTopIcd(List<Dictionary<string, JsonElement>> rows) =>
        rows
            .Where(r => !string.IsNullOrEmpty(GetString(r, "MaICD")))
            .GroupBy(r => (MaIcd: GetString(r, "MaICD")!, TenIcd: GetString(r, "TenICD") ?? ""))
            .Select(g => new M02IcdDto(g.Key.MaIcd, g.Key.TenIcd, g.Count()))
            .OrderByDescending(x => x.SoLuong)
            .Take(10)
            .ToList();

    private static List<M02BenhNhanDto> BuildDanhSachNoiTru(List<Dictionary<string, JsonElement>> rows) =>
        rows
            .Where(r => GetString(r, "TrangThai") == "DangNoiTru")
            .Select(r => new M02BenhNhanDto(
                Mrn:         GetString(r, "MRN")         ?? "",
                TenBenhNhan: GetString(r, "TenBenhNhan") ?? "",
                TenKhoa:     GetString(r, "TenKhoa")     ?? "",
                SoGiuong:    GetString(r, "SoGiuong"),
                NgayNhap:    GetString(r, "NgayNhap"),
                NgayXuat:    GetString(r, "NgayXuat"),
                DoiTuong:    GetString(r, "DoiTuong"),
                TrangThai:   GetString(r, "TrangThai"),
                ChanDoan:    GetString(r, "ChanDoan")))
            .ToList();

    private static string? GetString(Dictionary<string, JsonElement> dict, string key) =>
        dict.TryGetValue(key, out var val) ? val.ToString() : null;

    private static int GetInt(Dictionary<string, JsonElement> dict, string key)
    {
        if (!dict.TryGetValue(key, out var val)) return 0;
        return val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var i) ? i : 0;
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, out var d) ? d : null;
}
