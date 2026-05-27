using System.Text.Json;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.Reports;

public sealed record GetReportQuery(
    string ReportCode,
    string? SourceSystem,
    string? RecordType,
    DateTime? From,
    DateTime? To) : IRequest<Result<ReportDto>>;

public sealed class GetReportHandler(IStagingRecordRepository records)
    : IRequestHandler<GetReportQuery, Result<ReportDto>>
{
    private static readonly HashSet<string> SupportedCodes =
        ["chi-phi-theo-khoa", "benh-nhan-theo-khoa", "tong-hop-nguon"];

    public async Task<Result<ReportDto>> Handle(GetReportQuery request, CancellationToken ct)
    {
        if (!SupportedCodes.Contains(request.ReportCode))
            return Result.Failure<ReportDto>(
                Error.NotFound($"Report '{request.ReportCode}' not supported. Supported: {string.Join(", ", SupportedCodes)}"));

        var matched = await records.GetMatchedAsync(
            request.SourceSystem, request.RecordType, request.From, request.To, ct);

        // Parse CanonicalPayload của từng record thành dict để aggregate.
        var rows = matched
            .Where(r => !string.IsNullOrEmpty(r.CanonicalPayload))
            .Select(r =>
            {
                try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(r.CanonicalPayload!) ?? []; }
                catch { return new Dictionary<string, JsonElement>(); }
            })
            .Where(d => d.Count > 0)
            .ToList();

        return request.ReportCode switch
        {
            "chi-phi-theo-khoa"   => BuildChiPhiTheoKhoa(rows),
            "benh-nhan-theo-khoa" => BuildBenhNhanTheoKhoa(rows),
            "tong-hop-nguon"      => BuildTongHopNguon(matched.Select(r => r.SourceSystem).ToList(), rows),
            _                     => Result.Failure<ReportDto>(Error.NotFound(request.ReportCode))
        };
    }

    private static Result<ReportDto> BuildChiPhiTheoKhoa(List<Dictionary<string, JsonElement>> rows)
    {
        var grouped = rows
            .GroupBy(r => GetString(r, "TenKhoa") ?? "(unknown)")
            .Select(g => new { TenKhoa = g.Key, SoBenhNhan = g.Count(), TongChiPhi = g.Sum(r => GetDecimal(r, "TongChiPhi")) })
            .OrderByDescending(x => x.TongChiPhi)
            .ToList();

        return new ReportDto(
            "chi-phi-theo-khoa", "Chi phi theo khoa", DateTime.UtcNow,
            Columns: [
                new("TenKhoa",    "Ten khoa",    "string"),
                new("SoBenhNhan", "So benh nhan","number"),
                new("TongChiPhi", "Tong chi phi","currency")
            ],
            Rows: grouped.Select(x => new ReportRowDto(new Dictionary<string, object?>
            {
                ["TenKhoa"]    = x.TenKhoa,
                ["SoBenhNhan"] = x.SoBenhNhan,
                ["TongChiPhi"] = x.TongChiPhi
            })).ToList(),
            Summary: new Dictionary<string, object?>
            {
                ["TotalRecords"] = grouped.Sum(x => x.SoBenhNhan),
                ["TotalChiPhi"]  = grouped.Sum(x => x.TongChiPhi)
            });
    }

    private static Result<ReportDto> BuildBenhNhanTheoKhoa(List<Dictionary<string, JsonElement>> rows)
    {
        var grouped = rows
            .GroupBy(r => (TenKhoa: GetString(r, "TenKhoa") ?? "(unknown)", TrangThai: GetString(r, "TrangThai") ?? "(unknown)"))
            .Select(g => new { g.Key.TenKhoa, g.Key.TrangThai, SoBenhNhan = g.Count() })
            .OrderBy(x => x.TenKhoa).ThenBy(x => x.TrangThai)
            .ToList();

        return new ReportDto(
            "benh-nhan-theo-khoa", "Benh nhan theo khoa", DateTime.UtcNow,
            Columns: [
                new("TenKhoa",    "Ten khoa",    "string"),
                new("TrangThai",  "Trang thai",  "string"),
                new("SoBenhNhan", "So benh nhan","number")
            ],
            Rows: grouped.Select(x => new ReportRowDto(new Dictionary<string, object?>
            {
                ["TenKhoa"]    = x.TenKhoa,
                ["TrangThai"]  = x.TrangThai,
                ["SoBenhNhan"] = x.SoBenhNhan
            })).ToList(),
            Summary: new Dictionary<string, object?> { ["TotalRecords"] = grouped.Sum(x => x.SoBenhNhan) });
    }

    private static Result<ReportDto> BuildTongHopNguon(
        List<string> sourceSystems,
        List<Dictionary<string, JsonElement>> rows)
    {
        var grouped = sourceSystems
            .Zip(rows, (s, r) => (SourceSystem: s, Row: r))
            .GroupBy(x => x.SourceSystem)
            .Select(g => new { SourceSystem = g.Key, SoBenhNhan = g.Count(), TongChiPhi = g.Sum(x => GetDecimal(x.Row, "TongChiPhi")) })
            .OrderByDescending(x => x.SoBenhNhan)
            .ToList();

        return new ReportDto(
            "tong-hop-nguon", "Tong hop theo nguon", DateTime.UtcNow,
            Columns: [
                new("SourceSystem", "Nguon du lieu","string"),
                new("SoBenhNhan",   "So benh nhan", "number"),
                new("TongChiPhi",   "Tong chi phi", "currency")
            ],
            Rows: grouped.Select(x => new ReportRowDto(new Dictionary<string, object?>
            {
                ["SourceSystem"] = x.SourceSystem,
                ["SoBenhNhan"]   = x.SoBenhNhan,
                ["TongChiPhi"]   = x.TongChiPhi
            })).ToList(),
            Summary: new Dictionary<string, object?>
            {
                ["TotalRecords"] = grouped.Sum(x => x.SoBenhNhan),
                ["TotalChiPhi"]  = grouped.Sum(x => x.TongChiPhi)
            });
    }

    private static string? GetString(Dictionary<string, JsonElement> dict, string key) =>
        dict.TryGetValue(key, out var val) ? val.ToString() : null;

    private static decimal GetDecimal(Dictionary<string, JsonElement> dict, string key)
    {
        if (!dict.TryGetValue(key, out var val)) return 0;
        return val.ValueKind == JsonValueKind.Number && val.TryGetDecimal(out var d) ? d : 0;
    }
}
