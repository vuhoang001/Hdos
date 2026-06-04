using Hdos.LakehouseService.Application.DTOs;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.LakehouseService.Application.Features.Snapshots.GetSnapshots;

public sealed record GetSnapshotsQuery(string Namespace, int Limit = 100) : IRequest<Result<List<LakehouseSnapshotDto>>>;
