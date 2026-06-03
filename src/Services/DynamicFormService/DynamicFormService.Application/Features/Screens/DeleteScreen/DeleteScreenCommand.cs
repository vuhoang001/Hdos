using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Screens.DeleteScreen;

public sealed record DeleteScreenCommand(string ModuleCode, string ScreenCode) : IRequest<Result>;

public sealed class DeleteScreenCommandHandler(
    IFormScreenRepository  screens,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<DeleteScreenCommand, Result>
{
    public async Task<Result> Handle(DeleteScreenCommand request, CancellationToken ct)
    {
        var screen = await screens.GetByCodeAsync(request.ModuleCode, request.ScreenCode, ct);
        if (screen is null)
            return Result.Failure(Error.NotFound($"Screen '{request.ScreenCode}'"));

        screens.Remove(screen);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
