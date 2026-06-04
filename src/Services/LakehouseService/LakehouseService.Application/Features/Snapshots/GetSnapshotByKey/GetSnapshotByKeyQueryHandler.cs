using System.Text.Json;
using Hdos.LakehouseService.Application.DTOs;
using Hdos.LakehouseService.Domain.Errors;
using Hdos.LakehouseService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.LakehouseService.Application.Features.Snapshots.GetSnapshotByKey;

public sealed class GetSnapshotByKeyQueryHandler(ILakehouseSnapshotRepository repository)
    : IRequestHandler<GetSnapshotByKeyQuery, Result<LakehouseSnapshotDto>>
{
    public async Task<Result<LakehouseSnapshotDto>> Handle(GetSnapshotByKeyQuery request, CancellationToken ct)
    {
        var snapshot = await repository.GetLatestAsync(request.Namespace, request.Key, ct);
        if (snapshot is null)
            return Result.Failure<LakehouseSnapshotDto>(LakehouseErrors.SnapshotNotFound);

        var payload = JsonSerializer.Deserialize<object>(snapshot.Payload) ?? snapshot.Payload;
        return Result.Success(new LakehouseSnapshotDto(
            snapshot.Id,
            snapshot.Namespace,
            snapshot.BusinessKey,
            payload,
            snapshot.JobId,
            snapshot.ReceivedAt));
    }
}
