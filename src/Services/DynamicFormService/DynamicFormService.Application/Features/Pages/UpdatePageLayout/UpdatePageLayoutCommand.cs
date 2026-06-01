using System.Text.Json;
using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Pages.UpdatePageLayout;

public sealed record UpdatePageLayoutCommand(
    Guid               PageId,
    FormPageLayout     Layout) : IRequest<Result>;

public sealed class UpdatePageLayoutCommandValidator : AbstractValidator<UpdatePageLayoutCommand>
{
    public UpdatePageLayoutCommandValidator()
    {
        RuleFor(x => x.PageId).NotEmpty();
        RuleFor(x => x.Layout).NotNull();
        RuleFor(x => x.Layout.Rows).NotNull();
    }
}

public sealed class UpdatePageLayoutCommandHandler(
    IFormPageRepository    pages,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<UpdatePageLayoutCommand, Result>
{
    public async Task<Result> Handle(UpdatePageLayoutCommand request, CancellationToken ct)
    {
        var page = await pages.GetByIdAsync(request.PageId, ct);
        if (page is null)
            return Result.Failure(Error.NotFound($"Page '{request.PageId}'"));

        var layoutJson = JsonSerializer.Serialize(request.Layout);
        page.UpdateLayout(layoutJson);
        await uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
