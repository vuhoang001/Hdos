using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.M01Service.Application.Features.Dashboard;

public sealed record GetDashboardSummaryQuery() : IRequest<Result<DashboardSummaryDto>>;

public sealed class GetDashboardSummaryHandler(IM01ReadRepository repo)
    : IRequestHandler<GetDashboardSummaryQuery, Result<DashboardSummaryDto>>
{
    public async Task<Result<DashboardSummaryDto>> Handle(
        GetDashboardSummaryQuery request, CancellationToken ct)
    {
        var snap = await repo.GetDashboardSnapshotAsync(ct);
        if (snap is null) return Result.Failure<DashboardSummaryDto>(Error.NotFound("DashboardSnapshot"));

        var dto = new DashboardSummaryDto(
            snap.TongLuotKham,
            snap.ChoKhamTbPhut,
            snap.ChoMaxPhut,
            new TriageBucketDto(snap.TriageP1, snap.TriageP2, snap.TriageP3),
            snap.TrongNguong,
            snap.UpdatedAtUtc ?? snap.CreatedAtUtc);
        return dto;
    }
}
