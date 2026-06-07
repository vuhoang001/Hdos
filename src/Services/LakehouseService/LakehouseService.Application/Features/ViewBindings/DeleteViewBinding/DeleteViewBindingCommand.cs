using Hdos.LakehouseService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.LakehouseService.Application.Features.ViewBindings.DeleteViewBinding;

public sealed record DeleteViewBindingCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteViewBindingHandler(IViewBindingRepository repo)
    : IRequestHandler<DeleteViewBindingCommand, Result>
{
    public async Task<Result> Handle(DeleteViewBindingCommand request, CancellationToken ct)
    {
        var entity = await repo.GetByIdAsync(request.Id, ct);
        if (entity is null)
            return Result.Failure(Error.NotFound($"ViewBinding '{request.Id}'"));

        await repo.RemoveAsync(entity, ct);
        await repo.SaveChangesAsync(ct);

        return Result.Success();
    }
}
