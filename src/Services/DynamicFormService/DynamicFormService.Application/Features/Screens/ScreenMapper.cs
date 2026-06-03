using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;

namespace Hdos.DynamicFormService.Application.Features.Screens;

internal static class ScreenMapper
{
    internal static FormScreenDto ToDto(FormScreen s, int tabCount) => new(
        s.Id, s.ModuleCode, s.Code, s.Title, s.Description, s.Status.ToString(),
        s.SortOrder, tabCount, s.CreatedAtUtc);

    internal static FormScreenTabDto ToTabDto(FormScreenTab t) => new(
        t.Id, t.Label, t.Slug, t.SortOrder, t.IsDefault, t.Widgets.Count);

    internal static FormScreenWidgetDto ToWidgetDto(FormScreenWidget w) => new(
        w.Id, w.WidgetKey, w.WidgetType.ToString(), w.GridX, w.GridY, w.GridW, w.GridH,
        w.ConfigJson, w.ReferenceId);
}
