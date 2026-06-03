using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Screens;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Tabs.UpdateTab;

public sealed record UpdateTabCommand(
    string ModuleCode,
    string ScreenCode,
    Guid   TabId,
    string Label,
    int    SortOrder,
    bool   IsDefault) : IRequest<Result<FormScreenTabDto>>;

public sealed class UpdateTabCommandValidator : AbstractValidator<UpdateTabCommand>
{
    public UpdateTabCommandValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateTabCommandHandler(
    IFormScreenRepository  screens,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<UpdateTabCommand, Result<FormScreenTabDto>>
{
    public async Task<Result<FormScreenTabDto>> Handle(UpdateTabCommand request, CancellationToken ct)
    {
        var screen = await screens.GetByCodeWithTabsAsync(request.ModuleCode, request.ScreenCode, ct);
        if (screen is null)
            return Result.Failure<FormScreenTabDto>(Error.NotFound($"Screen '{request.ScreenCode}'"));

        var tab = screen.Tabs.FirstOrDefault(t => t.Id == request.TabId);
        if (tab is null)
            return Result.Failure<FormScreenTabDto>(Error.NotFound($"Tab '{request.TabId}'"));

        tab.Update(request.Label, request.SortOrder, request.IsDefault);
        await uow.SaveChangesAsync(ct);
        return ScreenMapper.ToTabDto(tab);
    }
}
