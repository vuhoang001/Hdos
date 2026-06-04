using System.Text.Json;
using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.DynamicFormService.Domain.ValueObjects;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Forms.AddField;

public sealed record AddFieldCommand(
    Guid FormTemplateId,
    string Key,
    string Label,
    FieldType FieldType,
    int Order,
    bool Required,
    FieldWidth Width,
    string? Placeholder,
    string? HelpText,
    List<FieldOptionDto>? Options,
    List<ValidationRuleDto>? ValidationRules,
    ConditionalLogicDto? ConditionalLogic,
    string? DataBindingExpression = null,
    string? DisplayFormat = null,
    bool IsReadOnly = false) : IRequest<Result<FormFieldDto>>;

public sealed class AddFieldCommandValidator : AbstractValidator<AddFieldCommand>
{
    public AddFieldCommandValidator()
    {
        RuleFor(x => x.FormTemplateId).NotEmpty();
        RuleFor(x => x.Key)
            .NotEmpty()
            .MaximumLength(100)
            .Matches(@"^[a-z0-9_]+$").WithMessage("Field key chỉ được chứa chữ thường, số và dấu gạch dưới.");
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Order).GreaterThanOrEqualTo(0);
    }
}

public sealed class AddFieldCommandHandler(
    IFormTemplateRepository templates,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<AddFieldCommand, Result<FormFieldDto>>
{
    public async Task<Result<FormFieldDto>> Handle(AddFieldCommand request, CancellationToken ct)
    {
        var template = await templates.GetByIdAsync(request.FormTemplateId, includeFields: true, ct);
        if (template is null)
            return Result.Failure<FormFieldDto>(Error.NotFound("FormTemplate"));

        var options = request.Options?.Select(o => new FieldOption(o.Label, o.Value)).ToList();
        var rules = request.ValidationRules?.Select(r => new ValidationRule(r.Type, r.Value, r.ErrorMessage)).ToList();
        var conditional = request.ConditionalLogic is { } cl
            ? new ConditionalLogic(cl.SourceFieldKey, cl.Operator, cl.Value, cl.Action)
            : null;
        var dataBinding = request.DataBindingExpression is not null
            ? new DataBinding(request.DataBindingExpression, request.DisplayFormat)
            : null;

        FormField field;
        try
        {
            field = template.AddField(
                request.Key, request.Label, request.FieldType, request.Order,
                request.Required, request.Width, request.Placeholder, request.HelpText,
                options, rules, conditional, dataBinding, request.IsReadOnly);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<FormFieldDto>(Error.Conflict(ex.Message));
        }

        await uow.SaveChangesAsync(ct);
        return MapField(field);
    }

    private static FormFieldDto MapField(FormField f) => new(
        f.Id, f.Key, f.Label,
        f.FieldType.ToString().ToLowerInvariant(),
        f.Order, f.Required,
        f.Width.ToString().ToLowerInvariant(),
        f.Placeholder, f.HelpText,
        f.OptionsJson is not null
            ? JsonSerializer.Deserialize<List<FieldOptionDto>>(f.OptionsJson)
            : null,
        f.ValidationRulesJson is not null
            ? JsonSerializer.Deserialize<List<ValidationRuleDto>>(f.ValidationRulesJson)
            : null,
        f.ConditionalLogicJson is not null
            ? JsonSerializer.Deserialize<ConditionalLogicDto>(f.ConditionalLogicJson)
            : null,
        f.DataBindingJson is not null
            ? JsonSerializer.Deserialize<DataBindingDto>(f.DataBindingJson)
            : null,
        f.IsReadOnly);
}