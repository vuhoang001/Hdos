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

        // Fetch tuần tự — DbContext không thread-safe; Task.WhenAll trên cùng repo
        // sẽ throw "A second operation was started on this context instance".
        var data = new Dictionary<string, List<Dictionary<string, JsonElement>>>(config.RecordTypes.Count);
        foreach (var rt in config.RecordTypes)
        {
            var raw  = await _records.GetMatchedAsync(sourceSystem, rt, null, null, ct);
            data[rt] = ParsePayloads(raw);
        }

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
