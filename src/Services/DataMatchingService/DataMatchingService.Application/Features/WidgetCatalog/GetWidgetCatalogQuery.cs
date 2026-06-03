using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.WidgetCatalog;

public sealed record GetWidgetCatalogQuery(string? Category) : IRequest<Result<List<WidgetCatalogDto>>>;

public sealed class GetWidgetCatalogHandler(IWidgetCatalogRepository repo)
    : IRequestHandler<GetWidgetCatalogQuery, Result<List<WidgetCatalogDto>>>
{
    public async Task<Result<List<WidgetCatalogDto>>> Handle(GetWidgetCatalogQuery request, CancellationToken ct)
    {
        var widgets = await repo.GetAllAsync(request.Category, ct);

        var dtos = widgets.Select(w => new WidgetCatalogDto(
            w.ChartType,
            w.Category,
            w.Label,
            w.Description,
            w.Icon,
            w.GetRequiredColumns(),
            w.GetOptionalColumns(),
            w.GetCompatibleWith(),
            w.SortOrder)).ToList();

        return dtos;
    }
}
