using System.Text.Json;
using Hdos.LakehouseService.Application.DTOs;
using Hdos.LakehouseService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.LakehouseService.Application.Features.Snapshots.GetSnapshots;

public sealed class GetSnapshotsQueryHandler(ILakehouseSnapshotRepository repository)
    : IRequestHandler<GetSnapshotsQuery, Result<List<LakehouseSnapshotDto>>>
{
    public async Task<Result<List<LakehouseSnapshotDto>>> Handle(GetSnapshotsQuery request, CancellationToken ct)
    {
        var snapshots = await repository.GetByNamespaceAsync(request.Namespace, request.Limit, ct);

        var dtos = snapshots.Select(s =>
        {
            var payload = JsonSerializer.Deserialize<object>(s.Payload) ?? s.Payload;
            return new LakehouseSnapshotDto(s.Id, s.Namespace, s.BusinessKey, payload, s.JobId, s.ReceivedAt);
        }).ToList();

        return Result.Success(dtos);
    }
}
