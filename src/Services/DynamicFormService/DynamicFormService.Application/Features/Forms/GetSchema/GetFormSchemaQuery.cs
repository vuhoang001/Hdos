using System.Text.Json;
using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.DynamicFormService.Domain.ValueObjects;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Forms.GetSchema;

/// <summary>
/// Endpoint BDUI chính — trả FormSchemaDto để frontend render form động.
/// </summary>
public sealed record GetFormSchemaQuery(string ModuleCode, string FormKey)
    : IRequest<Result<FormSchemaDto>>;

public sealed class GetFormSchemaQueryValidator : AbstractValidator<GetFormSchemaQuery>
{
    public GetFormSchemaQueryValidator()
    {
        RuleFor(x => x.ModuleCode).NotEmpty();
        RuleFor(x => x.FormKey).NotEmpty();
    }
}

public sealed class GetFormSchemaQueryHandler(IFormTemplateRepository templates)
    : IRequestHandler<GetFormSchemaQuery, Result<FormSchemaDto>>
{
    public async Task<Result<FormSchemaDto>> Handle(GetFormSchemaQuery request, CancellationToken ct)
    {
        var template = await templates.GetByModuleAndKeyAsync(
            request.ModuleCode, request.FormKey, includeFields: true, ct);

        if (template is null)
            return Result.Failure<FormSchemaDto>(
                Error.NotFound($"Form '{request.FormKey}' trong module '{request.ModuleCode}'"));

        if (template.Status != Domain.Enums.FormStatus.Published)
            return Result.Failure<FormSchemaDto>(
                Error.Conflict("Form chưa được publish."));

        return MapToSchema(template);
    }

    private static FormSchemaDto MapToSchema(FormTemplate t)
    {
        var settings = JsonSerializer.Deserialize<FormSettings>(t.SettingsJson)!;

        var fields = t.Fields
            .OrderBy(f => f.Order)
            .Select(f => new FormFieldDto(
                f.Id,
                f.Key,
                f.Label,
                f.FieldType.ToString().ToLowerInvariant(),
                f.Order,
                f.Required,
                f.Width.ToString().ToLowerInvariant(),
                f.Placeholder,
                f.HelpText,
                f.OptionsJson is not null
                    ? JsonSerializer.Deserialize<List<FieldOptionDto>>(f.OptionsJson) : null,
                f.ValidationRulesJson is not null
                    ? JsonSerializer.Deserialize<List<ValidationRuleDto>>(f.ValidationRulesJson) : null,
                f.ConditionalLogicJson is not null
                    ? JsonSerializer.Deserialize<ConditionalLogicDto>(f.ConditionalLogicJson) : null,
                f.DataBindingJson is not null
                    ? JsonSerializer.Deserialize<DataBindingDto>(f.DataBindingJson) : null,
                f.IsReadOnly))
            .ToList();

        var settingsDto = new FormSettingsDto(
            settings.SubmitButtonLabel,
            settings.SuccessMessage,
            settings.AllowMultipleSubmissions);

        return new FormSchemaDto(t.Id, t.ModuleCode, t.Key, t.Name, t.Description, t.Version, fields, settingsDto);
    }
}
