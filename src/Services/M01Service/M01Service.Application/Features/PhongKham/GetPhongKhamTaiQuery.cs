using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Application.Mapping;
using Hdos.M01Service.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.M01Service.Application.Features.PhongKham;

public sealed record GetPhongKhamTaiQuery() : IRequest<Result<IReadOnlyList<PhongKhamDto>>>;

public sealed class GetPhongKhamTaiHandler(IM01ReadRepository repo)
    : IRequestHandler<GetPhongKhamTaiQuery, Result<IReadOnlyList<PhongKhamDto>>>
{
    public async Task<Result<IReadOnlyList<PhongKhamDto>>> Handle(
        GetPhongKhamTaiQuery request, CancellationToken ct)
    {
        var rows = await repo.ListPhongKhamAsync(ct);
        IReadOnlyList<PhongKhamDto> data = rows.Select(p => p.ToDto()).ToList();
        return Result.Success(data);
    }
}
