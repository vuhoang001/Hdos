using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Pages.PublishPage;

public sealed record PublishPageCommand(Guid PageId) : IRequest<Result>;

public sealed class PublishPageCommandHandler(
    IFormPageRepository    pages,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<PublishPageCommand, Result>
{
    public async Task<Result> Handle(PublishPageCommand request, CancellationToken ct)
    {
        var page = await pages.GetByIdAsync(request.PageId, ct);
        if (page is null)
            return Result.Failure(Error.NotFound($"Page '{request.PageId}'"));

        page.Publish();
        await uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
