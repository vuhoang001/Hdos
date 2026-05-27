using System.Text.Json;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.Records;

// Query linh hoạt để lấy danh sách record theo bộ lọc bất kỳ.
// field/value: lọc theo một field cụ thể trong CanonicalPayload.
// Ví dụ: field=TenKhoa&value=Tim Mach → chỉ lấy record có TenKhoa = "Tim Mach".
public sealed record GetRecordsQuery(
    string? SourceSystem,
    string? RecordType,
    string? Field,
    string? Value,
    DateTime? From,
    DateTime? To,
    int Limit = 200) : IRequest<Result<List<StagingRecordDto>>>;

public sealed class GetRecordsHandler(IStagingRecordRepository records)
    : IRequestHandler<GetRecordsQuery, Result<List<StagingRecordDto>>>
{
    public async Task<Result<List<StagingRecordDto>>> Handle(GetRecordsQuery request, CancellationToken ct)
    {
        var limit = Math.Clamp(request.Limit, 1, 1000);

        var matched = await records.GetFilteredAsync(
            request.SourceSystem,
            request.RecordType,
            request.Field,
            request.Value,
            request.From,
            request.To,
            limit,
            ct);

        var dtos = matched.Select(r => new StagingRecordDto(
            r.Id,
            r.SourceSystem,
            r.RecordType,
            r.BusinessKey,
            r.Status.ToString(),
            r.CanonicalPayload,
            r.ReceivedAt,
            r.ProcessedAt)).ToList();

        return dtos;
    }
}
