using System.Text.Json;
using Hdos.SharedKernel;

namespace Hdos.DataMatchingService.Domain.Entities;

public sealed class SourceProfile : BaseEntity<Guid>
{
    public string SourceSystem { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string BusinessKeyField { get; private set; } = default!;
    public string FieldMappingsJson { get; private set; } = default!;

    private SourceProfile() { }

    public static SourceProfile Create(
        string sourceSystem,
        string displayName,
        string businessKeyField,
        Dictionary<string, string> mappings)
    {
        return new SourceProfile
        {
            Id               = Guid.NewGuid(),
            SourceSystem     = sourceSystem,
            DisplayName      = displayName,
            BusinessKeyField = businessKeyField,
            FieldMappingsJson = JsonSerializer.Serialize(mappings)
        };
    }

    public void Update(
        string displayName,
        string businessKeyField,
        Dictionary<string, string> mappings)
    {
        DisplayName      = displayName;
        BusinessKeyField = businessKeyField;
        FieldMappingsJson = JsonSerializer.Serialize(mappings);
        UpdatedAtUtc     = DateTime.UtcNow;
    }

    public Dictionary<string, string> GetMappings() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(FieldMappingsJson)
        ?? new Dictionary<string, string>();
}
