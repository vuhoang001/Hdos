using System.Text.Json;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Events;
using Hdos.DynamicFormService.Domain.ValueObjects;
using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Entities;

public sealed class FormTemplate : AggregateRoot<Guid>
{
    private readonly List<FormField> _fields = new();

    public Guid ModuleId { get; private set; }
    public string ModuleCode { get; private set; } = null!; // denormalized để filter không cần JOIN
    public string Key { get; private set; } = null!;        // slug, unique trong module
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public FormStatus Status { get; private set; }
    public int Version { get; private set; }
    public string SettingsJson { get; private set; } = null!; // jsonb: FormSettings

    public IReadOnlyCollection<FormField> Fields => _fields.AsReadOnly();

    private FormTemplate()
    {
    }

    public static FormTemplate Create(
        Guid moduleId,
        string moduleCode,
        string key,
        string name,
        string? description,
        FormSettings settings)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Form key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Form name is required.", nameof(name));

        return new FormTemplate
        {
            Id           = Guid.NewGuid(),
            ModuleId     = moduleId,
            ModuleCode   = moduleCode,
            Key          = key.Trim().ToLowerInvariant(),
            Name         = name.Trim(),
            Description  = description?.Trim(),
            Status       = FormStatus.Draft,
            Version      = 1,
            SettingsJson = JsonSerializer.Serialize(settings)
        };
    }

    public FormField AddField(
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
        if (Status == FormStatus.Published)
            throw new InvalidOperationException("Cannot modify a published form. Archive it first.");
        if (_fields.Any(f => f.Key == key.Trim().ToLowerInvariant()))
            throw new InvalidOperationException($"Field '{key}' already exists in this form.");

        var field = FormField.Create(Id, key, label, fieldType, order, required, width,
                                     placeholder, helpText, options, rules, conditional,
                                     dataBinding, isReadOnly);
        _fields.Add(field);
        UpdatedAtUtc = DateTime.UtcNow;
        return field;
    }

    public void Publish()
    {
        if (!_fields.Any())
            throw new InvalidOperationException("Cannot publish a form without fields.");
        if (Status == FormStatus.Archived)
            throw new InvalidOperationException("Cannot publish an archived form.");

        Status = FormStatus.Published;
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new FormPublishedDomainEvent(Id, ModuleCode, Key));
    }

    public void Archive()
    {
        Status       = FormStatus.Archived;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Update(string name, string? description, FormSettings settings)
    {
        if (Status == FormStatus.Published)
            throw new InvalidOperationException("Cannot modify a published form. Archive it first.");

        Name         = name.Trim();
        Description  = description?.Trim();
        SettingsJson = JsonSerializer.Serialize(settings);
        UpdatedAtUtc = DateTime.UtcNow;
    }
}