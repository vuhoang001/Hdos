using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Screens.CreateScreen;

public sealed record CreateScreenCommand(
    string  ModuleCode,
    string  Code,
    string  Title,
    string? Description,
    int     SortOrder = 0) : IRequest<Result<FormScreenDto>>;

public sealed class CreateScreenCommandValidator : AbstractValidator<CreateScreenCommand>
{
    public CreateScreenCommandValidator()
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

public sealed class CreateScreenCommandHandler(
    IFormModuleRepository  modules,
    IFormScreenRepository  screens,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<CreateScreenCommand, Result<FormScreenDto>>
{
    public async Task<Result<FormScreenDto>> Handle(CreateScreenCommand request, CancellationToken ct)
    {
        var module = await modules.GetByCodeAsync(request.ModuleCode, ct);
        if (module is null)
            return Result.Failure<FormScreenDto>(Error.NotFound($"Module '{request.ModuleCode}'"));

        var exists = await screens.ExistsByCodeAsync(request.ModuleCode, request.Code, ct);
        if (exists)
            return Result.Failure<FormScreenDto>(
                Error.Conflict($"Screen '{request.Code}' đã tồn tại trong module '{request.ModuleCode}'."));

        var screen = FormScreen.Create(module.Id, module.Code, request.Code, request.Title, request.Description, request.SortOrder);
        screens.Add(screen);
        await uow.SaveChangesAsync(ct);

        return ScreenMapper.ToDto(screen, tabCount: 0);
    }
}
