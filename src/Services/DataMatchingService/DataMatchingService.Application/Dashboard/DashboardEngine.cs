using System.Text.Json;
using Hdos.DataMatchingService.Domain.Repositories;

namespace Hdos.DataMatchingService.Application.Dashboard;

public sealed class DashboardEngine
{
    private readonly IStagingRecordRepository _records;
    private readonly Dictionary<string, DashboardConfig> _registry;

    public DashboardEngine(IStagingRecordRepository records, IEnumerable<DashboardConfig> configs)
    {
        _records  = records;
        _registry = configs.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> AvailableCodes => [.. _registry.Keys.Order()];

    public async Task<DashboardResult?> ExecuteAsync(
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

        var sections = config.BuildSections(data, reportDate);
        return new DashboardResult(code, config.Title, reportDate, DateTime.UtcNow, sections);
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

public sealed record DashboardResult(
    string ReportCode,
    string ReportTitle,
    DateOnly ReportDate,
    DateTime GeneratedAt,
    List<DashboardSection> Sections);
