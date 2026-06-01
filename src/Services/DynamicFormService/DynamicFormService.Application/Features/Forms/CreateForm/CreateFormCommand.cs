using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.DynamicFormService.Domain.ValueObjects;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Forms.CreateForm;

public sealed record CreateFormCommand(
    string  ModuleCode,
    string  Key,
    string  Name,
    string? Description,
    string  SubmitButtonLabel       = "Gửi",
    string  SuccessMessage          = "Đã gửi form thành công",
    bool    AllowMultipleSubmissions = true) : IRequest<Result<FormTemplateDto>>;

public sealed class CreateFormCommandValidator : AbstractValidator<CreateFormCommand>
{
    public CreateFormCommandValidator()
    {
        RuleFor(x => x.ModuleCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Key)
            .NotEmpty()
            .MaximumLength(100)
            .Matches(@"^[a-z0-9\-]+$").WithMessage("Key chỉ được chứa chữ thường, số và dấu gạch ngang.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
        RuleFor(x => x.SubmitButtonLabel).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SuccessMessage).NotEmpty().MaximumLength(300);
    }
}

public sealed class CreateFormCommandHandler(
    IFormModuleRepository   modules,
    IFormTemplateRepository templates,
    IDynamicFormUnitOfWork  uow)
    : IRequestHandler<CreateFormCommand, Result<FormTemplateDto>>
{
    public async Task<Result<FormTemplateDto>> Handle(CreateFormCommand request, CancellationToken ct)
    {
        var module = await modules.GetByCodeAsync(request.ModuleCode, ct);
        if (module is null)
            return Result.Failure<FormTemplateDto>(Error.NotFound($"Module '{request.ModuleCode}'"));

        var keyExists = await templates.ExistsByKeyInModuleAsync(module.Id, request.Key, ct);
        if (keyExists)
            return Result.Failure<FormTemplateDto>(
                Error.Conflict($"Form '{request.Key}' đã tồn tại trong module '{request.ModuleCode}'."));

        var settings = new FormSettings(request.SubmitButtonLabel, request.SuccessMessage, request.AllowMultipleSubmissions);
        var template = FormTemplate.Create(module.Id, module.Code, request.Key, request.Name, request.Description, settings);

        await templates.AddAsync(template, ct);
        await uow.SaveChangesAsync(ct);

        return Map(template);
    }

    private static FormTemplateDto Map(FormTemplate t) => new(
        t.Id, t.ModuleCode, t.Key, t.Name, t.Description, t.Status.ToString(), t.Version, 0, t.CreatedAtUtc);
}
