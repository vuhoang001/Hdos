using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Forms.GetForms;

public sealed record GetFormsByModuleQuery(string ModuleCode) : IRequest<Result<List<FormTemplateDto>>>;

public sealed class GetFormsByModuleQueryHandler(IFormTemplateRepository templates)
    : IRequestHandler<GetFormsByModuleQuery, Result<List<FormTemplateDto>>>
{
    public async Task<Result<List<FormTemplateDto>>> Handle(GetFormsByModuleQuery request, CancellationToken ct)
    {
        var list = await templates.GetByModuleCodeAsync(request.ModuleCode, ct);
        return list.Select(Map).ToList();
    }

    private static FormTemplateDto Map(FormTemplate t) => new(
        t.Id, t.ModuleCode, t.Key, t.Name, t.Description,
        t.Status.ToString(), t.Version, t.Fields.Count, t.CreatedAtUtc);
}