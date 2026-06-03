using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Screens.PublishScreen;

public sealed record PublishScreenCommand(string ModuleCode, string ScreenCode) : IRequest<Result>;

public sealed class PublishScreenCommandHandler(
    IFormScreenRepository  screens,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<PublishScreenCommand, Result>
{
    public async Task<Result> Handle(PublishScreenCommand request, CancellationToken ct)
    {
        var screen = await screens.GetByCodeAsync(request.ModuleCode, request.ScreenCode, ct);
        if (screen is null)
            return Result.Failure(Error.NotFound($"Screen '{request.ScreenCode}'"));

        screen.Publish();
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
