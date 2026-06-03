using System.Text.Json;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Screens.GetScreenLayout;

public sealed record GetScreenLayoutQuery(string ModuleCode, string ScreenCode) : IRequest<Result<ScreenLayoutDto>>;

public sealed class GetScreenLayoutQueryHandler(
    IFormScreenRepository   screens,
    IFormTemplateRepository templates)
    : IRequestHandler<GetScreenLayoutQuery, Result<ScreenLayoutDto>>
{
    public async Task<Result<ScreenLayoutDto>> Handle(GetScreenLayoutQuery request, CancellationToken ct)
    {
        var screen = await screens.GetWithTabsAndWidgetsAsync(request.ModuleCode, request.ScreenCode, ct);
        if (screen is null)
            return Result.Failure<ScreenLayoutDto>(
                Error.NotFound($"Screen '{request.ScreenCode}' không tồn tại hoặc chưa publish."));

        var tabs = new List<ScreenLayoutTabDto>();

        foreach (var tab in screen.Tabs.OrderBy(t => t.SortOrder))
        {
            var widgets = new List<ScreenLayoutWidgetDto>();

            foreach (var w in tab.Widgets)
            {
                FormSchemaDto? formSchema = null;

                if (w.WidgetType.Equals("FormSection", StringComparison.OrdinalIgnoreCase) && w.ReferenceId.HasValue)
                {
                    var template = await templates.GetByIdAsync(w.ReferenceId.Value, includeFields: true, ct);
                    if (template is not null)
                        formSchema = HydrateFormSchema(template);
                }

                object? config = null;
                if (!string.IsNullOrWhiteSpace(w.ConfigJson) && w.ConfigJson != "{}")
                {
                    try { config = JsonSerializer.Deserialize<object>(w.ConfigJson); }
                    catch { config = null; }
                }

                widgets.Add(new ScreenLayoutWidgetDto(
                    w.WidgetKey, w.WidgetType,
                    w.GridX, w.GridY, w.GridW, w.GridH,
                    config, w.ReferenceId, formSchema));
            }

            tabs.Add(new ScreenLayoutTabDto(tab.Id, tab.Label, tab.Slug, tab.SortOrder, tab.IsDefault, widgets));
        }

        return Result.Success(new ScreenLayoutDto(
            screen.Id, screen.ModuleCode, screen.Code, screen.Title, screen.Description,
            tabs, DateTime.UtcNow));
    }

    private static FormSchemaDto HydrateFormSchema(Domain.Entities.FormTemplate t)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<Domain.ValueObjects.FormSettings>(t.SettingsJson)
                       ?? new Domain.ValueObjects.FormSettings("Gửi", "Đã gửi thành công", true);

        var fields = t.Fields.OrderBy(f => f.Order).Select(f => new FormFieldDto(
            f.Id, f.Key, f.Label, f.FieldType.ToString(), f.Order, f.Required,
            f.Width.ToString(), f.Placeholder, f.HelpText,
            DeserializeOrNull<List<Domain.ValueObjects.FieldOption>>(f.OptionsJson)
                ?.Select(o => new FieldOptionDto(o.Label, o.Value)).ToList(),
            DeserializeOrNull<List<Domain.ValueObjects.ValidationRule>>(f.ValidationRulesJson)
                ?.Select(r => new ValidationRuleDto(r.Type, r.Value, r.ErrorMessage)).ToList(),
            DeserializeOrNull<Domain.ValueObjects.ConditionalLogic>(f.ConditionalLogicJson) is { } cl
                ? new ConditionalLogicDto(cl.SourceFieldKey, cl.Operator.ToString(), cl.Value, cl.Action.ToString())
                : null)).ToList();

        return new FormSchemaDto(t.Id, t.ModuleCode, t.Key, t.Name, t.Description, t.Version, fields,
            new FormSettingsDto(settings.SubmitButtonLabel, settings.SuccessMessage, settings.AllowMultipleSubmissions));
    }

    private static T? DeserializeOrNull<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return null; }
    }
}
