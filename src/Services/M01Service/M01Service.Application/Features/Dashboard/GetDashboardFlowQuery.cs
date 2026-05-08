using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.M01Service.Application.Features.Dashboard;

public sealed record GetDashboardFlowQuery() : IRequest<Result<DashboardFlowDto>>;

public sealed class GetDashboardFlowHandler
    : IRequestHandler<GetDashboardFlowQuery, Result<DashboardFlowDto>>
{
    private readonly IM01ReadRepository _repo;

    public GetDashboardFlowHandler(IM01ReadRepository repo) => _repo = repo;

    public async Task<Result<DashboardFlowDto>> Handle(
        GetDashboardFlowQuery request, CancellationToken ct)
    {
        var snap = await _repo.GetDashboardSnapshotAsync(ct);
        if (snap is null) return Result.Failure<DashboardFlowDto>(Error.NotFound("DashboardSnapshot"));

        var dto = new DashboardFlowDto(
            snap.FlowDangKy,
            snap.FlowChoKham,
            snap.FlowDangKham,
            snap.FlowChoCls,
            snap.FlowNhanKq,
            snap.FlowKeDonNv,
            snap.FlowHoanThanh,
            snap.FlowTatTbPhut,
            snap.UpdatedAtUtc ?? snap.CreatedAtUtc);
        return dto;
    }
}
