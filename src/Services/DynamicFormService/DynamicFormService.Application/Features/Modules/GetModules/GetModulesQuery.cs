using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Screens;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Modules.GetModules;

/// <param name="PageStatus">null = Published only; "all" = mọi status; "draft"/"archived" = filter theo status đó</param>
public sealed record GetModulesQuery(string? PageStatus = null) : IRequest<Result<List<FormModuleDto>>>;

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

        FormStatus? statusFilter = request.PageStatus?.ToLowerInvariant() switch
        {
            null or "published" => FormStatus.Published,
            "draft"             => FormStatus.Draft,
            "archived"          => FormStatus.Archived,
            "all"               => null,
            _                   => FormStatus.Published
        };

        var allPages = await screens.GetByModuleCodesAsync(codes, statusFilter, ct);

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
