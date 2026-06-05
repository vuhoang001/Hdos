using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Modules.DeleteModule;

public sealed record DeleteModuleCommand(string Code) : IRequest<Result>;

public sealed class DeleteModuleCommandHandler(
    IFormModuleRepository    modules,
    IFormTemplateRepository  forms,
    IFormScreenRepository    screens,
    IDynamicFormUnitOfWork   uow)
    : IRequestHandler<DeleteModuleCommand, Result>
{
    public async Task<Result> Handle(DeleteModuleCommand request, CancellationToken ct)
    {
        var module = await modules.GetByCodeAsync(request.Code, ct);
        if (module is null)
            return Result.Failure(Error.NotFound($"Module '{request.Code}'"));

        var existingForms = await forms.GetByModuleCodeAsync(request.Code, ct);
        if (existingForms.Count > 0)
            return Result.Failure(Error.Conflict(
                $"Không thể xóa module '{request.Code}' vì còn {existingForms.Count} form. Hãy xóa form trước."));

        var existingScreens = await screens.GetByModuleAsync(request.Code, ct);
        if (existingScreens.Count > 0)
            return Result.Failure(Error.Conflict(
                $"Không thể xóa module '{request.Code}' vì còn {existingScreens.Count} screen. Hãy xóa screen trước."));

        modules.Remove(module);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
