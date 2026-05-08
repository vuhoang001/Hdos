using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Application.Mapping;
using Hdos.M01Service.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.M01Service.Application.Features.BenhNhan;

public sealed record GetBenhNhansQuery(int Page = 1, int PageSize = 20)
    : IRequest<Result<BenhNhanPageDto>>;

public sealed class GetBenhNhansHandler
    : IRequestHandler<GetBenhNhansQuery, Result<BenhNhanPageDto>>
{
    private readonly IM01ReadRepository _repo;

    public GetBenhNhansHandler(IM01ReadRepository repo) => _repo = repo;

    public async Task<Result<BenhNhanPageDto>> Handle(GetBenhNhansQuery request, CancellationToken ct)
    {
        var (items, total) = await _repo.ListBenhNhansAsync(request.Page, request.PageSize, ct);
        var page = request.Page < 1 ? 1 : request.Page;
        return new BenhNhanPageDto(total, page, items.Select(b => b.ToDto()).ToList());
    }
}
