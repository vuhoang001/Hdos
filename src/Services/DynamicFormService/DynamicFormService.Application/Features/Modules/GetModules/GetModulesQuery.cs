using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Modules.GetModules;

public sealed record GetModulesQuery : IRequest<Result<List<FormModuleDto>>>;

public sealed class GetModulesQueryHandler(IFormModuleRepository modules)
    : IRequestHandler<GetModulesQuery, Result<List<FormModuleDto>>>
{
    public async Task<Result<List<FormModuleDto>>> Handle(GetModulesQuery request, CancellationToken ct)
    {
        var list = await modules.GetAllActiveAsync(ct);
        return list.Select(m => new FormModuleDto(
            m.Id, m.Code, m.Name, m.Description, m.Status.ToString(), 0, m.CreatedAtUtc))
            .ToList();
    }
}
