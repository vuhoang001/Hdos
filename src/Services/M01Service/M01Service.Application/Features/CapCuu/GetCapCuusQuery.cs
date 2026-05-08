using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Application.Mapping;
using Hdos.M01Service.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.M01Service.Application.Features.CapCuu;

public sealed record GetCapCuusQuery() : IRequest<Result<CapCuuListDto>>;

public sealed class GetCapCuusHandler : IRequestHandler<GetCapCuusQuery, Result<CapCuuListDto>>
{
    private readonly IM01ReadRepository _repo;

    public GetCapCuusHandler(IM01ReadRepository repo) => _repo = repo;

    public async Task<Result<CapCuuListDto>> Handle(GetCapCuusQuery request, CancellationToken ct)
    {
        var rows = await _repo.ListCapCuusAsync(ct);
        var data = rows.Select(r => r.ToDto()).ToList();
        return new CapCuuListDto(true, data.Count, data);
    }
}
