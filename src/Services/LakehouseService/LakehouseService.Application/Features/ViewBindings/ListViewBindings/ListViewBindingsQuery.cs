using Hdos.LakehouseService.Application.DTOs;
using Hdos.LakehouseService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.LakehouseService.Application.Features.ViewBindings.ListViewBindings;

public sealed record ListViewBindingsQuery(bool ActiveOnly) : IRequest<Result<List<ViewBindingDto>>>;

public sealed class ListViewBindingsQueryHandler(IViewBindingRepository repo)
    : IRequestHandler<ListViewBindingsQuery, Result<List<ViewBindingDto>>>
{
    public async Task<Result<List<ViewBindingDto>>> Handle(ListViewBindingsQuery request, CancellationToken ct)
    {
        var entities = request.ActiveOnly
            ? await repo.ListActiveAsync(ct)
            : await repo.ListAsync(ct);

        var dtos = entities.Select(b => new ViewBindingDto(
            b.Id, b.ViewName, b.SourceSystem, b.RecordType,
            b.BusinessKeyColumn, b.UpdatedAtColumn, b.PollIntervalSeconds,
            b.IsActive, b.CreatedAtUtc, b.UpdatedAtUtc)).ToList();

        return Result.Success(dtos);
    }
}
