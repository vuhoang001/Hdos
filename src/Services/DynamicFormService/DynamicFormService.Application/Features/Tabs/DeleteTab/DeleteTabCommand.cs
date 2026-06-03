using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Tabs.DeleteTab;

public sealed record DeleteTabCommand(string ModuleCode, string ScreenCode, Guid TabId) : IRequest<Result>;

public sealed class DeleteTabCommandHandler(
    IFormScreenRepository  screens,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<DeleteTabCommand, Result>
{
    public async Task<Result> Handle(DeleteTabCommand request, CancellationToken ct)
    {
        var screen = await screens.GetByCodeWithTabsAsync(request.ModuleCode, request.ScreenCode, ct);
        if (screen is null)
            return Result.Failure(Error.NotFound($"Screen '{request.ScreenCode}'"));

        if (screen.Tabs.All(t => t.Id != request.TabId))
            return Result.Failure(Error.NotFound($"Tab '{request.TabId}'"));

        screen.RemoveTab(request.TabId);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
