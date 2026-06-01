using FluentValidation;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Forms.PublishForm;

public sealed record PublishFormCommand(Guid FormTemplateId) : IRequest<Result>;

public sealed class PublishFormCommandValidator : AbstractValidator<PublishFormCommand>
{
    public PublishFormCommandValidator() => RuleFor(x => x.FormTemplateId).NotEmpty();
}

public sealed class PublishFormCommandHandler(
    IFormTemplateRepository templates,
    IDynamicFormUnitOfWork  uow)
    : IRequestHandler<PublishFormCommand, Result>
{
    public async Task<Result> Handle(PublishFormCommand request, CancellationToken ct)
    {
        var template = await templates.GetByIdAsync(request.FormTemplateId, includeFields: true, ct);
        if (template is null)
            return Result.Failure(Error.NotFound("FormTemplate"));

        try
        {
            template.Publish();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict(ex.Message));
        }

        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
