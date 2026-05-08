using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Application.Mapping;
using Hdos.M01Service.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.M01Service.Application.Features.Forecast;

public sealed record GetAiForecastQuery() : IRequest<Result<AiForecastDto>>;

public sealed class GetAiForecastHandler : IRequestHandler<GetAiForecastQuery, Result<AiForecastDto>>
{
    private readonly IM01ReadRepository _repo;

    public GetAiForecastHandler(IM01ReadRepository repo) => _repo = repo;

    public async Task<Result<AiForecastDto>> Handle(GetAiForecastQuery request, CancellationToken ct)
    {
        var (meta, entries) = await _repo.GetForecastAsync(ct);
        if (meta is null)
            return Result.Failure<AiForecastDto>(Error.NotFound("ForecastMeta"));

        var dto = new AiForecastDto(
            meta.ModelVersion,
            meta.CaoDiemDuKien,
            meta.DoChinhXacMae,
            entries.Select(e => e.ToDto()).ToList());
        return dto;
    }
}
