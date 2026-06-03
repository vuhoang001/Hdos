using FluentValidation;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Tabs.SaveTabWidgets;

public sealed record WidgetInput(
    string  WidgetKey,
    string  WidgetType,
    int     GridX,
    int     GridY,
    int     GridW,
    int     GridH,
    string? ConfigJson,
    Guid?   ReferenceId);

public sealed record SaveTabWidgetsCommand(
    string            ModuleCode,
    string            ScreenCode,
    Guid              TabId,
    List<WidgetInput> Widgets) : IRequest<Result<int>>;

public sealed class SaveTabWidgetsCommandValidator : AbstractValidator<SaveTabWidgetsCommand>
{
    public SaveTabWidgetsCommandValidator()
    {
        RuleFor(x => x.Widgets).NotNull();
        RuleForEach(x => x.Widgets).ChildRules(w =>
        {
            w.RuleFor(x => x.WidgetKey).NotEmpty().MaximumLength(100);
            w.RuleFor(x => x.WidgetType).NotEmpty()
             .Must(t => Enum.TryParse<WidgetType>(t, ignoreCase: true, out _))
             .WithMessage("WidgetType không hợp lệ.");
            w.RuleFor(x => x.GridW).GreaterThan(0);
            w.RuleFor(x => x.GridH).GreaterThan(0);
        });
        RuleFor(x => x.Widgets)
            .Must(ws => ws.Select(w => w.WidgetKey).Distinct().Count() == ws.Count)
            .WithMessage("Mỗi widgetKey phải là duy nhất trong tab.");
    }
}

public sealed class SaveTabWidgetsCommandHandler(
    IFormScreenRepository  screens,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<SaveTabWidgetsCommand, Result<int>>
{
    public async Task<Result<int>> Handle(SaveTabWidgetsCommand request, CancellationToken ct)
    {
        var screen = await screens.GetByCodeAsync(request.ModuleCode, request.ScreenCode, ct);
        if (screen is null)
            return Result.Failure<int>(Error.NotFound($"Screen '{request.ScreenCode}'"));

        var tab = await screens.GetTabWithWidgetsAsync(screen.Id, request.TabId, ct);
        if (tab is null)
            return Result.Failure<int>(Error.NotFound($"Tab '{request.TabId}'"));

        var newWidgets = request.Widgets.Select(w =>
        {
            Enum.TryParse<WidgetType>(w.WidgetType, ignoreCase: true, out var type);
            return FormScreenWidget.Create(
                tab.Id, w.WidgetKey, type,
                w.GridX, w.GridY, w.GridW, w.GridH,
                w.ConfigJson ?? "{}", w.ReferenceId);
        }).ToList();

        tab.ReplaceWidgets(newWidgets);
        await uow.SaveChangesAsync(ct);

        return Result.Success(newWidgets.Count);
    }
}
