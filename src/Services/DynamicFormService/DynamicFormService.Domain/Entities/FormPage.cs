using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Events;
using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Entities;

public sealed class FormPage : AggregateRoot<Guid>
{
    public Guid           ModuleId    { get; private set; }
    public string         ModuleCode  { get; private set; } = null!;
    public string         Code        { get; private set; } = null!;  // slug, unique trong module
    public string         Title       { get; private set; } = null!;
    public string?        Description { get; private set; }
    public FormPageStatus Status      { get; private set; }
    public string         LayoutJson  { get; private set; } = null!;  // jsonb: FormPageLayout

    private FormPage() { }

    public static FormPage Create(
        Guid    moduleId,
        string  moduleCode,
        string  code,
        string  title,
        string? description)
    {
        if (string.IsNullOrWhiteSpace(code))  throw new ArgumentException("Page code is required.",  nameof(code));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Page title is required.", nameof(title));

        return new FormPage
        {
            Id          = Guid.NewGuid(),
            ModuleId    = moduleId,
            ModuleCode  = moduleCode,
            Code        = code.Trim().ToLowerInvariant(),
            Title       = title.Trim(),
            Description = description?.Trim(),
            Status      = FormPageStatus.Draft,
            LayoutJson  = """{"rows":[]}"""
        };
    }

    public void UpdateLayout(string layoutJson)
    {
        if (Status == FormPageStatus.Archived)
            throw new InvalidOperationException("Cannot modify an archived page.");

        LayoutJson   = layoutJson;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Publish()
    {
        if (Status == FormPageStatus.Archived)
            throw new InvalidOperationException("Cannot publish an archived page.");

        Status       = FormPageStatus.Published;
        UpdatedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new FormPagePublishedDomainEvent(Id, ModuleCode, Code));
    }

    public void Archive()
    {
        Status       = FormPageStatus.Archived;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
