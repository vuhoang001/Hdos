using Hdos.LakehouseService.Application.Charts.Sdui;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.Charts;

/// <summary>
/// Registry + lookup các <see cref="ILakehouseChartConfig"/> đã đăng ký DI.
/// Pattern y hệt SduiEngine bên DataMatching nhưng dùng <see cref="NpgsqlDataSource"/>
/// trực tiếp thay vì StagingRecord repository.
/// </summary>
public sealed class LakehouseChartBuilder
{
    private readonly NpgsqlDataSource _warehouse;
    private readonly Dictionary<string, ILakehouseChartConfig> _registry;

    public LakehouseChartBuilder(NpgsqlDataSource warehouse, IEnumerable<ILakehouseChartConfig> configs)
    {
        _warehouse = warehouse;
        _registry  = configs.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> AvailableCodes => [.. _registry.Keys.Order()];

    public async Task<SduiPage?> BuildAsync(
        string            code,
        DateOnly?         date,
        IQueryCollection  query,
        CancellationToken ct)
    {
        if (!_registry.TryGetValue(code, out var config)) return null;
        var reportDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return await config.BuildAsync(_warehouse, reportDate, query, ct);
    }
}
