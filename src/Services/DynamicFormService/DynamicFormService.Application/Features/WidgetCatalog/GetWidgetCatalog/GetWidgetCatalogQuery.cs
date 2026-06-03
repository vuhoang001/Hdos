using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.WidgetCatalog.GetWidgetCatalog;

public sealed record GetWidgetCatalogQuery(string? Category) : IRequest<Result<List<WidgetCatalogItemDto>>>;

public sealed class GetWidgetCatalogQueryHandler(IWidgetCatalogRepository repo)
    : IRequestHandler<GetWidgetCatalogQuery, Result<List<WidgetCatalogItemDto>>>
{
    public async Task<Result<List<WidgetCatalogItemDto>>> Handle(GetWidgetCatalogQuery request, CancellationToken ct)
    {
        var widgets = await repo.GetAllAsync(request.Category, ct);

        var dtos = widgets.Select(w => new WidgetCatalogItemDto(
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
