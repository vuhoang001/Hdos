using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Application.Mapping;
using Hdos.M01Service.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.M01Service.Application.Features.CapCuu;

public sealed record GetCapCuusQuery() : IRequest<Result<CapCuuListDto>>;

public sealed class GetCapCuusHandler(IM01ReadRepository repo) : IRequestHandler<GetCapCuusQuery, Result<CapCuuListDto>>
{
    public async Task<Result<CapCuuListDto>> Handle(GetCapCuusQuery request, CancellationToken ct)
    {
        var rows = await repo.ListCapCuusAsync(ct);
        var data = rows.Select(r => r.ToDto()).ToList();
        return new CapCuuListDto(true, data.Count, data);
    }
}
