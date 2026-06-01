using FluentValidation;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Forms.ArchiveForm;

public sealed record ArchiveFormCommand(Guid FormTemplateId) : IRequest<Result>;

public sealed class ArchiveFormCommandValidator : AbstractValidator<ArchiveFormCommand>
{
    public ArchiveFormCommandValidator() => RuleFor(x => x.FormTemplateId).NotEmpty();
}

public sealed class ArchiveFormCommandHandler(
    IFormTemplateRepository templates,
    IDynamicFormUnitOfWork  uow)
    : IRequestHandler<ArchiveFormCommand, Result>
{
    public async Task<Result> Handle(ArchiveFormCommand request, CancellationToken ct)
    {
        var template = await templates.GetByIdAsync(request.FormTemplateId, includeFields: false, ct);
        if (template is null)
            return Result.Failure(Error.NotFound("FormTemplate"));

        template.Archive();
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
