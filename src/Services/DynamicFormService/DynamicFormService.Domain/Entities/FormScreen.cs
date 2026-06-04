using System.Text.Json;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Events;
using Hdos.DynamicFormService.Domain.ValueObjects;
using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Entities;

public sealed class FormScreen : AggregateRoot<Guid>
{
    private readonly List<FormScreenTab> _tabs = new();

    public Guid       ModuleId    { get; private set; }
    public string     ModuleCode  { get; private set; } = null!;
    public string     Code        { get; private set; } = null!;
    public string     Title       { get; private set; } = null!;
    public string?    Description { get; private set; }
    public FormStatus Status          { get; private set; }
    public int        SortOrder       { get; private set; }
    public string?    DataSourcesJson { get; private set; } // jsonb: List<DataSource>

    public IReadOnlyCollection<FormScreenTab> Tabs => _tabs.AsReadOnly();

    private FormScreen() { }

    public static FormScreen Create(
        Guid    moduleId,
        string  moduleCode,
        string  code,
        string  title,
        string? description,
        int     sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(code))  throw new ArgumentException("Screen code is required.",  nameof(code));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Screen title is required.", nameof(title));

        return new FormScreen
        {
            Id          = Guid.NewGuid(),
            ModuleId    = moduleId,
            ModuleCode  = moduleCode,
            Code        = code.Trim().ToLowerInvariant(),
            Title       = title.Trim(),
            Description = description?.Trim(),
            Status      = FormStatus.Draft,
            SortOrder   = sortOrder
        };
    }

    public void Update(string title, string? description, int sortOrder)
    {
        if (Status == FormStatus.Archived)
            throw new InvalidOperationException("Cannot modify an archived screen.");

        Title       = title.Trim();
        Description = description?.Trim();
        SortOrder   = sortOrder;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Publish()
    {
        if (Status == FormStatus.Archived)
            throw new InvalidOperationException("Cannot publish an archived screen.");

        Status       = FormStatus.Published;
        UpdatedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new FormScreenPublishedDomainEvent(Id, ModuleCode, Code));
    }

    public FormScreenTab AddTab(string label, string slug, int sortOrder, bool isDefault)
    {
        if (Status == FormStatus.Archived)
            throw new InvalidOperationException("Cannot add tab to an archived screen.");
        if (_tabs.Any(t => t.Slug == slug.Trim().ToLowerInvariant()))
            throw new InvalidOperationException($"Tab '{slug}' already exists in this screen.");

        var tab = FormScreenTab.Create(Id, label, slug, sortOrder, isDefault);
        _tabs.Add(tab);
        UpdatedAtUtc = DateTime.UtcNow;
        return tab;
    }

    public void RemoveTab(Guid tabId)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId)
                  ?? throw new InvalidOperationException($"Tab '{tabId}' not found.");
        _tabs.Remove(tab);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetDataSources(List<DataSource> sources)
    {
        if (Status == FormStatus.Archived)
            throw new InvalidOperationException("Cannot modify an archived screen.");

        DataSourcesJson = sources.Count > 0 ? JsonSerializer.Serialize(sources) : null;
        UpdatedAtUtc    = DateTime.UtcNow;
    }

    public void Archive()
    {
        Status       = FormStatus.Archived;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
