using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Pages.CreatePage;

public sealed record CreatePageCommand(
    string  ModuleCode,
    string  Code,
    string  Title,
    string? Description) : IRequest<Result<FormPageDto>>;

public sealed class CreatePageCommandValidator : AbstractValidator<CreatePageCommand>
{
    public CreatePageCommandValidator()
    {
        RuleFor(x => x.ModuleCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(100)
            .Matches(@"^[a-z0-9\-]+$").WithMessage("Code chỉ được chứa chữ thường, số và dấu gạch ngang.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
    }
}

public sealed class CreatePageCommandHandler(
    IFormModuleRepository modules,
    IFormPageRepository   pages,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<CreatePageCommand, Result<FormPageDto>>
{
    public async Task<Result<FormPageDto>> Handle(CreatePageCommand request, CancellationToken ct)
    {
        var module = await modules.GetByCodeAsync(request.ModuleCode, ct);
        if (module is null)
            return Result.Failure<FormPageDto>(Error.NotFound($"Module '{request.ModuleCode}'"));

        var exists = await pages.ExistsByCodeAsync(request.ModuleCode, request.Code, ct);
        if (exists)
            return Result.Failure<FormPageDto>(
                Error.Conflict($"Page '{request.Code}' đã tồn tại trong module '{request.ModuleCode}'."));

        var page = FormPage.Create(module.Id, module.Code, request.Code, request.Title, request.Description);
        pages.Add(page);
        await uow.SaveChangesAsync(ct);

        return Map(page);
    }

    internal static FormPageDto Map(FormPage p) => new(
        p.Id, p.ModuleCode, p.Code, p.Title, p.Description, p.Status.ToString(), p.CreatedAtUtc);
}
