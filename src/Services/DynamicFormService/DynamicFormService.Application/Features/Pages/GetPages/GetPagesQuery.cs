using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Pages.CreatePage;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Pages.GetPages;

public sealed record GetPagesByModuleQuery(string ModuleCode) : IRequest<Result<List<FormPageDto>>>;

public sealed class GetPagesByModuleQueryHandler(IFormPageRepository pages)
    : IRequestHandler<GetPagesByModuleQuery, Result<List<FormPageDto>>>
{
    public async Task<Result<List<FormPageDto>>> Handle(GetPagesByModuleQuery request, CancellationToken ct)
    {
        var list = await pages.GetByModuleAsync(request.ModuleCode, ct);
        return list.Select(CreatePageCommandHandler.Map).ToList();
    }
}
