using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Application.Mapping;
using Hdos.M01Service.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.M01Service.Application.Features.NhanSu;

public sealed record GetNhanSuTrucQuery() : IRequest<Result<IReadOnlyList<NhanSuTrucDto>>>;

public sealed class GetNhanSuTrucHandler(IM01ReadRepository repo)
    : IRequestHandler<GetNhanSuTrucQuery, Result<IReadOnlyList<NhanSuTrucDto>>>
{
    public async Task<Result<IReadOnlyList<NhanSuTrucDto>>> Handle(
        GetNhanSuTrucQuery request, CancellationToken ct)
    {
        var rows = await repo.ListNhanSuTrucAsync(ct);
        IReadOnlyList<NhanSuTrucDto> data = rows.Select(r => r.ToDto()).ToList();
        return Result.Success(data);
    }
}
