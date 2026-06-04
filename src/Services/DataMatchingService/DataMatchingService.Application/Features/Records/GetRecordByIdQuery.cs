using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.Records;

public sealed record GetRecordByIdQuery(Guid Id) : IRequest<Result<StagingRecordDto>>;

public sealed class GetRecordByIdHandler(IStagingRecordRepository records)
    : IRequestHandler<GetRecordByIdQuery, Result<StagingRecordDto>>
{
    public async Task<Result<StagingRecordDto>> Handle(GetRecordByIdQuery request, CancellationToken ct)
    {
        var record = await records.GetByIdAsync(request.Id, ct);
        if (record is null)
            return Result.Failure<StagingRecordDto>(Error.NotFound($"Record '{request.Id}'"));

        return Result.Success(new StagingRecordDto(
            record.Id,
            record.SourceSystem,
            record.RecordType,
            record.BusinessKey,
            record.Status.ToString(),
            record.CanonicalPayload,
            record.ReceivedAt,
            record.ProcessedAt));
    }
}
