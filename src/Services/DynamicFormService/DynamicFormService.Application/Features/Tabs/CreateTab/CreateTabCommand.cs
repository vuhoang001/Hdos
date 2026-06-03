using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Screens;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Tabs.CreateTab;

public sealed record CreateTabCommand(
    string ModuleCode,
    string ScreenCode,
    string Label,
    string Slug,
    int    SortOrder = 0,
    bool   IsDefault = false) : IRequest<Result<FormScreenTabDto>>;

public sealed class CreateTabCommandValidator : AbstractValidator<CreateTabCommand>
{
    public CreateTabCommandValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug)
            .NotEmpty()
            .MaximumLength(100)
            .Matches(@"^[a-z0-9\-]+$").WithMessage("Slug chỉ được chứa chữ thường, số và dấu gạch ngang.");
    }
}

public sealed class CreateTabCommandHandler(
    IFormScreenRepository  screens,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<CreateTabCommand, Result<FormScreenTabDto>>
{
    public async Task<Result<FormScreenTabDto>> Handle(CreateTabCommand request, CancellationToken ct)
    {
        var screen = await screens.GetByCodeWithTabsAsync(request.ModuleCode, request.ScreenCode, ct);
        if (screen is null)
            return Result.Failure<FormScreenTabDto>(Error.NotFound($"Screen '{request.ScreenCode}'"));

        FormScreenTab tab;
        try
        {
            tab = screen.AddTab(request.Label, request.Slug, request.SortOrder, request.IsDefault);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<FormScreenTabDto>(Error.Conflict(ex.Message));
        }

        await uow.SaveChangesAsync(ct);
        return ScreenMapper.ToTabDto(tab);
    }
}
