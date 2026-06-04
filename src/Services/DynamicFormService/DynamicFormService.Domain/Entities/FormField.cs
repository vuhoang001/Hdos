using System.Text.Json;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.ValueObjects;
using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Entities;

public sealed class FormField : BaseEntity<Guid>
{
    public Guid FormTemplateId { get; private set; }
    public string Key { get; private set; } = null!;
    public string Label { get; private set; } = null!;
    public FieldType FieldType { get; private set; }
    public int Order { get; private set; }
    public bool Required { get; private set; }
    public FieldWidth Width { get; private set; }
    public string? Placeholder { get; private set; }
    public string? HelpText { get; private set; }
    public string? OptionsJson { get; private set; }          // jsonb: List<FieldOption>
    public string? ValidationRulesJson { get; private set; }  // jsonb: List<ValidationRule>
    public string? ConditionalLogicJson { get; private set; } // jsonb: ConditionalLogic
    public string? DataBindingJson { get; private set; }      // jsonb: DataBinding
    public bool    IsReadOnly      { get; private set; }

    private FormField()
    {
    }

    internal static FormField Create(
        Guid formTemplateId,
        string key,
        string label,
        FieldType fieldType,
        int order,
        bool required,
        FieldWidth width,
        string? placeholder,
        string? helpText,
        List<FieldOption>? options,
        List<ValidationRule>? rules,
        ConditionalLogic? conditional,
        DataBinding? dataBinding = null,
        bool isReadOnly = false)
    {
        return new FormField
        {
            Id                   = Guid.NewGuid(),
            FormTemplateId       = formTemplateId,
            Key                  = key.Trim().ToLowerInvariant(),
            Label                = label.Trim(),
            FieldType            = fieldType,
            Order                = order,
            Required             = required,
            Width                = width,
            Placeholder          = placeholder?.Trim(),
            HelpText             = helpText?.Trim(),
            OptionsJson          = options is { Count: > 0 } ? JsonSerializer.Serialize(options) : null,
            ValidationRulesJson  = rules is { Count  : > 0 } ? JsonSerializer.Serialize(rules) : null,
            ConditionalLogicJson = conditional is not null ? JsonSerializer.Serialize(conditional) : null,
            DataBindingJson      = dataBinding is not null ? JsonSerializer.Serialize(dataBinding) : null,
            IsReadOnly           = isReadOnly
        };
    }

    public void Update(
        string label,
        int order,
        bool required,
        FieldWidth width,
        string? placeholder,
        string? helpText,
        List<FieldOption>? options,
        List<ValidationRule>? rules,
        ConditionalLogic? conditional,
        DataBinding? dataBinding = null,
        bool isReadOnly = false)
    {
        Label                = label.Trim();
        Order                = order;
        Required             = required;
        Width                = width;
        Placeholder          = placeholder?.Trim();
        HelpText             = helpText?.Trim();
        OptionsJson          = options is { Count: > 0 } ? JsonSerializer.Serialize(options) : null;
        ValidationRulesJson  = rules is { Count  : > 0 } ? JsonSerializer.Serialize(rules) : null;
        ConditionalLogicJson = conditional is not null ? JsonSerializer.Serialize(conditional) : null;
        DataBindingJson      = dataBinding is not null ? JsonSerializer.Serialize(dataBinding) : null;
        IsReadOnly           = isReadOnly;
        UpdatedAtUtc         = DateTime.UtcNow;
    }
}