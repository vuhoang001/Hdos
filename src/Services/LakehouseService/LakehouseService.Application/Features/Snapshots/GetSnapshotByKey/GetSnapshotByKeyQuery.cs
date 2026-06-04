using Hdos.LakehouseService.Application.DTOs;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.LakehouseService.Application.Features.Snapshots.GetSnapshotByKey;

public sealed record GetSnapshotByKeyQuery(string Namespace, string Key) : IRequest<Result<LakehouseSnapshotDto>>;
