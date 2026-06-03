using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Screens.GetPublishedScreensByModule;

public sealed record GetPublishedScreensByModuleQuery(string ModuleCode) : IRequest<Result<List<FormScreenDto>>>;

public sealed class GetPublishedScreensByModuleQueryHandler(IFormScreenRepository screens)
    : IRequestHandler<GetPublishedScreensByModuleQuery, Result<List<FormScreenDto>>>
{
    public async Task<Result<List<FormScreenDto>>> Handle(GetPublishedScreensByModuleQuery request, CancellationToken ct)
    {
        var list = await screens.GetPublishedByModuleAsync(request.ModuleCode, ct);
        return list.Select(s => ScreenMapper.ToDto(s, 0)).ToList();
    }
}
