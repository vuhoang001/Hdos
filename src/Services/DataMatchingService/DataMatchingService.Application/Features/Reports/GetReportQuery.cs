using System.Text.Json;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.Reports;

public sealed record GetReportQuery(
    string ReportCode,
    string? SourceSystem,
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
                Error.NotFound($"Report code '{request.ReportCode}' is not supported. " +
                               $"Supported: {string.Join(", ", SupportedCodes)}"));

        var matched = await records.GetMatchedAsync(
            request.SourceSystem, request.From, request.To, ct);

        var rows = matched
            .Where(r => !string.IsNullOrEmpty(r.CanonicalPayload))
            .Select(r =>
            {
                try
                {
                    return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(r.CanonicalPayload!)
                           ?? new Dictionary<string, JsonElement>();
                }
                catch
                {
                    return new Dictionary<string, JsonElement>();
                }
            })
            .Where(d => d.Count > 0)
            .ToList();

        return request.ReportCode switch
        {
            "chi-phi-theo-khoa"   => BuildChiPhiTheoKhoa(rows),
            "benh-nhan-theo-khoa" => BuildBenhNhanTheoKhoa(rows),
            "tong-hop-nguon"      => BuildTongHopNguon(matched
                .Select(r => r.SourceSystem).ToList(), rows),
            _                     => Result.Failure<ReportDto>(Error.NotFound(request.ReportCode))
        };
    }

    private static Result<ReportDto> BuildChiPhiTheoKhoa(
        List<Dictionary<string, JsonElement>> rows)
    {
        var grouped = rows
            .GroupBy(r => GetString(r, "TenKhoa") ?? "(unknown)")
            .Select(g => new
            {
                TenKhoa    = g.Key,
                SoBenhNhan = g.Count(),
                TongChiPhi = g.Sum(r => GetDecimal(r, "TongChiPhi"))
            })
            .OrderByDescending(x => x.TongChiPhi)
            .ToList();

        var reportRows = grouped
            .Select(x => new ReportRowDto(new Dictionary<string, object?>
            {
                ["TenKhoa"]    = x.TenKhoa,
                ["SoBenhNhan"] = x.SoBenhNhan,
                ["TongChiPhi"] = x.TongChiPhi
            }))
            .ToList();

        var summary = new Dictionary<string, object?>
        {
            ["TotalRecords"] = grouped.Sum(x => x.SoBenhNhan),
            ["TotalChiPhi"]  = grouped.Sum(x => x.TongChiPhi)
        };

        return new ReportDto(
            ReportCode:   "chi-phi-theo-khoa",
            ReportName:   "Chi phi theo khoa",
            GeneratedAt:  DateTime.UtcNow,
            Columns:
            [
                new ReportColumnDto("TenKhoa",    "Ten khoa",   "string"),
                new ReportColumnDto("SoBenhNhan",  "So benh nhan", "number"),
                new ReportColumnDto("TongChiPhi",  "Tong chi phi", "currency")
            ],
            Rows:    reportRows,
            Summary: summary);
    }

    private static Result<ReportDto> BuildBenhNhanTheoKhoa(
        List<Dictionary<string, JsonElement>> rows)
    {
        var grouped = rows
            .GroupBy(r => (
                TenKhoa:   GetString(r, "TenKhoa") ?? "(unknown)",
                TrangThai: GetString(r, "TrangThai") ?? "(unknown)"))
            .Select(g => new
            {
                g.Key.TenKhoa,
                g.Key.TrangThai,
                SoBenhNhan = g.Count()
            })
            .OrderBy(x => x.TenKhoa)
            .ThenBy(x => x.TrangThai)
            .ToList();

        var reportRows = grouped
            .Select(x => new ReportRowDto(new Dictionary<string, object?>
            {
                ["TenKhoa"]    = x.TenKhoa,
                ["TrangThai"]  = x.TrangThai,
                ["SoBenhNhan"] = x.SoBenhNhan
            }))
            .ToList();

        var summary = new Dictionary<string, object?>
        {
            ["TotalRecords"] = grouped.Sum(x => x.SoBenhNhan)
        };

        return new ReportDto(
            ReportCode:  "benh-nhan-theo-khoa",
            ReportName:  "Benh nhan theo khoa",
            GeneratedAt: DateTime.UtcNow,
            Columns:
            [
                new ReportColumnDto("TenKhoa",    "Ten khoa",    "string"),
                new ReportColumnDto("TrangThai",  "Trang thai",  "string"),
                new ReportColumnDto("SoBenhNhan", "So benh nhan", "number")
            ],
            Rows:    reportRows,
            Summary: summary);
    }

    private static Result<ReportDto> BuildTongHopNguon(
        List<string> sourceSystems,
        List<Dictionary<string, JsonElement>> rows)
    {
        var grouped = sourceSystems
            .Zip(rows, (s, r) => (SourceSystem: s, Row: r))
            .GroupBy(x => x.SourceSystem)
            .Select(g => new
            {
                SourceSystem = g.Key,
                SoBenhNhan   = g.Count(),
                TongChiPhi   = g.Sum(x => GetDecimal(x.Row, "TongChiPhi"))
            })
            .OrderByDescending(x => x.SoBenhNhan)
            .ToList();

        var reportRows = grouped
            .Select(x => new ReportRowDto(new Dictionary<string, object?>
            {
                ["SourceSystem"] = x.SourceSystem,
                ["SoBenhNhan"]   = x.SoBenhNhan,
                ["TongChiPhi"]   = x.TongChiPhi
            }))
            .ToList();

        var summary = new Dictionary<string, object?>
        {
            ["TotalRecords"] = grouped.Sum(x => x.SoBenhNhan),
            ["TotalChiPhi"]  = grouped.Sum(x => x.TongChiPhi)
        };

        return new ReportDto(
            ReportCode:  "tong-hop-nguon",
            ReportName:  "Tong hop theo nguon",
            GeneratedAt: DateTime.UtcNow,
            Columns:
            [
                new ReportColumnDto("SourceSystem", "Nguon du lieu", "string"),
                new ReportColumnDto("SoBenhNhan",   "So benh nhan",  "number"),
                new ReportColumnDto("TongChiPhi",   "Tong chi phi",  "currency")
            ],
            Rows:    reportRows,
            Summary: summary);
    }

    private static string? GetString(Dictionary<string, JsonElement> dict, string key) =>
        dict.TryGetValue(key, out var val) ? val.ToString() : null;

    private static decimal GetDecimal(Dictionary<string, JsonElement> dict, string key)
    {
        if (!dict.TryGetValue(key, out var val)) return 0;
        return val.ValueKind == JsonValueKind.Number && val.TryGetDecimal(out var d) ? d : 0;
    }
}
