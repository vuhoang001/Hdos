using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Modules.GetModules;

public sealed record GetModulesQuery : IRequest<Result<List<FormModuleDto>>>;

public sealed class GetModulesQueryHandler(
    IFormModuleRepository   modules,
    IFormTemplateRepository templates,
    IFormScreenRepository   screens)
    : IRequestHandler<GetModulesQuery, Result<List<FormModuleDto>>>
{
    public async Task<Result<List<FormModuleDto>>> Handle(GetModulesQuery request, CancellationToken ct)
    {
        var list  = await modules.GetAllActiveAsync(ct);
        var codes = list.Select(m => m.Code).ToList();

        var formCounts   = await templates.GetPublishedCountsAsync(codes, ct);
        var screenCounts = await screens.GetPublishedCountsAsync(codes, ct);

        return list.Select(m => new FormModuleDto(
            m.Id, m.Code, m.Name, m.Description, m.Status.ToString(),
            formCounts.GetValueOrDefault(m.Code),
            screenCounts.GetValueOrDefault(m.Code),
            m.CreatedAtUtc)).ToList();
    }
}
