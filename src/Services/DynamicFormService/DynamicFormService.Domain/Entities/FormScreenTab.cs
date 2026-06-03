using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Entities;

public sealed class FormScreenTab : BaseEntity<Guid>
{
    private readonly List<FormScreenWidget> _widgets = new();

    public Guid   ScreenId  { get; private set; }
    public string Label     { get; private set; } = null!;
    public string Slug      { get; private set; } = null!;
    public int    SortOrder { get; private set; }
    public bool   IsDefault { get; private set; }

    public IReadOnlyCollection<FormScreenWidget> Widgets => _widgets.AsReadOnly();

    private FormScreenTab() { }

    public static FormScreenTab Create(Guid screenId, string label, string slug, int sortOrder, bool isDefault)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Tab label is required.", nameof(label));
        if (string.IsNullOrWhiteSpace(slug))  throw new ArgumentException("Tab slug is required.",  nameof(slug));

        return new FormScreenTab
        {
            Id        = Guid.NewGuid(),
            ScreenId  = screenId,
            Label     = label.Trim(),
            Slug      = slug.Trim().ToLowerInvariant(),
            SortOrder = sortOrder,
            IsDefault = isDefault
        };
    }

    public void Update(string label, int sortOrder, bool isDefault)
    {
        Label     = label.Trim();
        SortOrder = sortOrder;
        IsDefault = isDefault;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ReplaceWidgets(IEnumerable<FormScreenWidget> widgets)
    {
        _widgets.Clear();
        _widgets.AddRange(widgets);
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
