using System.Text.Json;
using Hdos.DataMatchingService.Domain.Repositories;

namespace Hdos.DataMatchingService.Application.Sdui;

public sealed class SduiEngine
{
    private readonly IStagingRecordRepository _records;
    private readonly Dictionary<string, SduiPageConfig> _registry;

    public SduiEngine(IStagingRecordRepository records, IEnumerable<SduiPageConfig> configs)
    {
        _records  = records;
        _registry = configs.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> AvailableCodes => [.. _registry.Keys.Order()];

    public async Task<SduiPage?> ExecuteAsync(
        string code,
        string? sourceSystem,
        DateOnly? date,
        CancellationToken ct)
    {
        if (!_registry.TryGetValue(code, out var config)) return null;

        var reportDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var fetchTasks = config.RecordTypes
            .Select(async rt =>
            {
                var raw = await _records.GetMatchedAsync(sourceSystem, rt, null, null, ct);
                return (RecordType: rt, Rows: ParsePayloads(raw));
            });

        var fetched = await Task.WhenAll(fetchTasks);
        var data    = fetched.ToDictionary(x => x.RecordType, x => x.Rows);

        return config.BuildPage(data, reportDate);
    }

    private static List<Dictionary<string, JsonElement>> ParsePayloads(
        IEnumerable<Domain.Entities.StagingRecord> source) =>
        source
            .Where(r => !string.IsNullOrEmpty(r.CanonicalPayload))
            .Select(r =>
            {
                try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(r.CanonicalPayload!) ?? []; }
                catch { return new Dictionary<string, JsonElement>(); }
            })
            .Where(d => d.Count > 0)
            .ToList();
}
