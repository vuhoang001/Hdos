using System.Text.Json;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.DynamicFormService.Domain.ValueObjects;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Pages.GetPageSchema;

// BDUI endpoint: trả toàn bộ page schema (layout + embedded form schemas) trong một lần gọi.
public sealed record GetPageSchemaQuery(string ModuleCode, string PageCode)
    : IRequest<Result<FormPageSchemaDto>>;

public sealed class GetPageSchemaQueryHandler(
    IFormPageRepository     pages,
    IFormTemplateRepository templates)
    : IRequestHandler<GetPageSchemaQuery, Result<FormPageSchemaDto>>
{
    public async Task<Result<FormPageSchemaDto>> Handle(GetPageSchemaQuery request, CancellationToken ct)
    {
        var page = await pages.GetByCodeAsync(request.ModuleCode, request.PageCode, ct);
        if (page is null)
            return Result.Failure<FormPageSchemaDto>(
                Error.NotFound($"Page '{request.PageCode}' trong module '{request.ModuleCode}'"));

        if (page.Status != FormPageStatus.Published)
            return Result.Failure<FormPageSchemaDto>(Error.Conflict("Page chưa được publish."));

        var layout = JsonSerializer.Deserialize<FormPageLayout>(page.LayoutJson)
                     ?? new FormPageLayout([]);

        // Collect tất cả FormKey cần hydrate
        var formKeys = layout.Rows
            .SelectMany(r => r.Components.OfType<FormSectionPageComponent>())
            .Select(c => c.FormKey)
            .Distinct()
            .ToList();

        // Load tất cả form cần thiết một lần
        var formMap = new Dictionary<string, FormSchemaDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in formKeys)
        {
            var tpl = await templates.GetByModuleAndKeyAsync(request.ModuleCode, key, includeFields: true, ct);
            if (tpl is { Status: FormStatus.Published })
                formMap[key] = MapFormSchema(tpl);
        }

        var rows = layout.Rows.Select(row =>
        {
            var comps = row.Components.Select<FormPageComponent, FormPageComponentDto>(c => c switch
            {
                FormSectionPageComponent fs => formMap.TryGetValue(fs.FormKey, out var schema)
                    ? new FormSectionPageComponentDto(fs.Span, fs.FormKey, fs.Title, schema)
                    : new FormSectionPageComponentDto(fs.Span, fs.FormKey, fs.Title,
                        new FormSchemaDto(Guid.Empty, request.ModuleCode, fs.FormKey,
                            "Form not found", null, 0, [], new FormSettingsDto("Gửi", "", true))),
                TextBlockPageComponent tb  => new TextBlockPageComponentDto(tb.Span, tb.Content, tb.Align),
                DividerPageComponent   dv  => new DividerPageComponentDto(dv.Span, dv.Label),
                _                         => throw new InvalidOperationException($"Unknown component type: {c.GetType().Name}")
            }).ToList();
            return new FormPageRowDto(comps);
        }).ToList();

        return new FormPageSchemaDto(page.Id, page.ModuleCode, page.Code, page.Title, page.Description, rows, DateTime.UtcNow);
    }

    private static FormSchemaDto MapFormSchema(FormTemplate t)
    {
        var settings = JsonSerializer.Deserialize<FormSettings>(t.SettingsJson)!;

        var fields = t.Fields
            .OrderBy(f => f.Order)
            .Select(f => new FormFieldDto(
                f.Id, f.Key, f.Label,
                f.FieldType.ToString().ToLowerInvariant(),
                f.Order, f.Required,
                f.Width.ToString().ToLowerInvariant(),
                f.Placeholder, f.HelpText,
                f.OptionsJson         is not null ? JsonSerializer.Deserialize<List<FieldOptionDto>>(f.OptionsJson) : null,
                f.ValidationRulesJson is not null ? JsonSerializer.Deserialize<List<ValidationRuleDto>>(f.ValidationRulesJson) : null,
                f.ConditionalLogicJson is not null ? JsonSerializer.Deserialize<ConditionalLogicDto>(f.ConditionalLogicJson) : null))
            .ToList();

        return new FormSchemaDto(
            t.Id, t.ModuleCode, t.Key, t.Name, t.Description, t.Version, fields,
            new FormSettingsDto(settings.SubmitButtonLabel, settings.SuccessMessage, settings.AllowMultipleSubmissions));
    }
}
