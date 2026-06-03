using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Screens.GetScreens;

public sealed record GetScreensQuery(string ModuleCode) : IRequest<Result<List<FormScreenDto>>>;

public sealed class GetScreensQueryHandler(IFormScreenRepository screens)
    : IRequestHandler<GetScreensQuery, Result<List<FormScreenDto>>>
{
    public async Task<Result<List<FormScreenDto>>> Handle(GetScreensQuery request, CancellationToken ct)
    {
        var list = await screens.GetByModuleAsync(request.ModuleCode, ct);
        var dtos  = list.Select(s => ScreenMapper.ToDto(s, s.Tabs.Count)).ToList();
        return Result.Success(dtos);
    }
}
