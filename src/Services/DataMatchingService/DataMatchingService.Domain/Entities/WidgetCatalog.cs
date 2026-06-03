using System.Text.Json;

namespace Hdos.DataMatchingService.Domain.Entities;

public sealed class WidgetCatalog
{
    public Guid Id { get; private set; }
    public string ChartType { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public string Label { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string Icon { get; private set; } = null!;
    public string RowSchema { get; private set; } = "{}";
    public string RequiredColumnsJson { get; private set; } = "[]";
    public string OptionalColumnsJson { get; private set; } = "[]";
    public string CompatibleWithJson { get; private set; } = "[]";
    public int SortOrder { get; private set; }

    private WidgetCatalog() { }

    public List<string> GetRequiredColumns() =>
        JsonSerializer.Deserialize<List<string>>(RequiredColumnsJson) ?? [];

    public List<string> GetOptionalColumns() =>
        JsonSerializer.Deserialize<List<string>>(OptionalColumnsJson) ?? [];

    public List<string> GetCompatibleWith() =>
        JsonSerializer.Deserialize<List<string>>(CompatibleWithJson) ?? [];
}
