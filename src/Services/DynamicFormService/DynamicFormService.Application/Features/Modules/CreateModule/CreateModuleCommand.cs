using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Modules.CreateModule;

public sealed record CreateModuleCommand(
    string  Code,
    string  Name,
    string? Description) : IRequest<Result<FormModuleDto>>;

public sealed class CreateModuleCommandValidator : AbstractValidator<CreateModuleCommand>
{
    public CreateModuleCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(50)
            .Matches(@"^[a-z0-9\-]+$").WithMessage("Code chỉ được chứa chữ thường, số và dấu gạch ngang.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
    }
}

public sealed class CreateModuleCommandHandler(
    IFormModuleRepository  modules,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<CreateModuleCommand, Result<FormModuleDto>>
{
    public async Task<Result<FormModuleDto>> Handle(CreateModuleCommand request, CancellationToken ct)
    {
        var exists = await modules.ExistsByCodeAsync(request.Code, ct);
        if (exists)
            return Result.Failure<FormModuleDto>(Error.Conflict($"Module với code '{request.Code}' đã tồn tại."));

        var module = FormModule.Create(request.Code, request.Name, request.Description);
        await modules.AddAsync(module, ct);
        await uow.SaveChangesAsync(ct);

        return Map(module);
    }

    private static FormModuleDto Map(FormModule m) => new(
        m.Id, m.Code, m.Name, m.Description, m.Status.ToString(), 0, m.CreatedAtUtc);
}
