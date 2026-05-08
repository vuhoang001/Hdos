using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Application.Mapping;
using Hdos.M01Service.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.M01Service.Application.Features.NhanSu;

public sealed record GetNhanSuTrucQuery() : IRequest<Result<IReadOnlyList<NhanSuTrucDto>>>;

public sealed class GetNhanSuTrucHandler
    : IRequestHandler<GetNhanSuTrucQuery, Result<IReadOnlyList<NhanSuTrucDto>>>
{
    private readonly IM01ReadRepository _repo;

    public GetNhanSuTrucHandler(IM01ReadRepository repo) => _repo = repo;

    public async Task<Result<IReadOnlyList<NhanSuTrucDto>>> Handle(
        GetNhanSuTrucQuery request, CancellationToken ct)
    {
        var rows = await _repo.ListNhanSuTrucAsync(ct);
        IReadOnlyList<NhanSuTrucDto> data = rows.Select(r => r.ToDto()).ToList();
        return Result.Success(data);
    }
}
