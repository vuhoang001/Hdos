using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Screens;
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

        var formCounts = await templates.GetPublishedCountsAsync(codes, ct);
        var allPages   = await screens.GetPublishedByModuleCodesAsync(codes, ct);

        var pagesByModule = allPages
            .GroupBy(s => s.ModuleCode)
            .ToDictionary(g => g.Key, g => g.Select(s => ScreenMapper.ToDto(s, 0)).ToList());

        return list.Select(m =>
        {
            var pages = pagesByModule.GetValueOrDefault(m.Code, []);
            return new FormModuleDto(
                m.Id, m.Code, m.Name, m.Description, m.Status.ToString(),
                formCounts.GetValueOrDefault(m.Code),
                pages.Count,
                pages,
                m.CreatedAtUtc);
        }).ToList();
    }
}
