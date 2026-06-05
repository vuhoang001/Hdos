using System.Diagnostics;
using System.Text.Json;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.Sync;

/// <summary>
/// Pull data từ warehouse external, publish mỗi row sang RabbitMQ để
/// <c>LakehouseDataReadyConsumer</c> upsert vào <c>LakehouseSnapshots</c>.
///
/// Demo Phase 1: chỉ hỗ trợ VIEW <c>api.encounter_activity_daily</c>.
/// Mỗi row → 1 event với <c>BusinessKey</c> composite <c>{date}|{department_id}|{room_id}</c>.
/// </summary>
public sealed class WarehouseViewSyncer(
    NpgsqlDataSource warehouseDataSource,
    IEventBus eventBus,
    ISyncStateRepository syncState,
    ILogger<WarehouseViewSyncer> logger) : IWarehouseViewSyncer
{
    private static readonly IReadOnlyDictionary<string, ViewMeta> SupportedViews =
        new Dictionary<string, ViewMeta>(StringComparer.OrdinalIgnoreCase)
        {
            ["encounter_activity_daily"] = new(
                Sql: """
                    SELECT date,
                           department_id,
                           room_id,
                           encounter_count,
                           distinct_patient_count,
                           inpatient_encounter_count,
                           discharged_encounter_count,
                           insured_encounter_count
                    FROM api.encounter_activity_daily
                    """,
                BuildKey: r =>
                {
                    var date = r.GetFieldValue<DateTime>(0).ToString("yyyy-MM-dd");
                    var dept = r.IsDBNull(1) ? "_" : r.GetValue(1).ToString();
                    var room = r.IsDBNull(2) ? "_" : r.GetValue(2).ToString();
                    return $"{date}|{dept}|{room}";
                }),
        };

    public async Task<SyncResult> SyncAsync(string viewName, CancellationToken ct)
    {
        if (!SupportedViews.TryGetValue(viewName, out var meta))
            throw new ArgumentException($"View '{viewName}' chưa được khai báo trong WarehouseViewSyncer.", nameof(viewName));

        var jobId  = $"sync-{viewName}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var sw     = Stopwatch.StartNew();
        var count  = 0;

        await using var conn = await warehouseDataSource.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(meta.Sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        // Lấy column names 1 lần để build JSON payload
        var fieldNames = Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.GetName(i))
            .ToArray();

        while (await reader.ReadAsync(ct))
        {
            var businessKey = meta.BuildKey(reader);
            var payload     = BuildPayloadJson(reader, fieldNames);

            await eventBus.PublishAsync(new LakehouseDataReadyIntegrationEvent(
                JobId:        jobId,
                Namespace:    viewName,
                BusinessKey:  businessKey,
                Payload:      payload,
                DownloadUrl:  null,
                TotalRecords: 1,
                ProcessedAt:  DateTime.UtcNow), ct);

            count++;
        }

        await syncState.UpsertAsync(viewName, count, jobId, ct);
        sw.Stop();

        logger.LogInformation(
            "Warehouse sync {View}: {Count} rows published in {Ms} ms (job {JobId})",
            viewName, count, sw.ElapsedMilliseconds, jobId);

        return new SyncResult(viewName, count, jobId, sw.Elapsed);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    private static string BuildPayloadJson(NpgsqlDataReader r, string[] fieldNames)
    {
        var dict = new Dictionary<string, object?>(r.FieldCount);
        for (var i = 0; i < r.FieldCount; i++)
        {
            dict[fieldNames[i]] = r.IsDBNull(i) ? null : NormalizeValue(r.GetValue(i));
        }
        return JsonSerializer.Serialize(dict, JsonOpts);
    }

    private static object? NormalizeValue(object value) => value switch
    {
        DateTime dt => dt.ToString("o"),                          // ISO 8601
        DateTimeOffset dto => dto.ToString("o"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        TimeSpan ts => ts.ToString(),
        Guid g => g.ToString(),
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => value
    };

    private sealed record ViewMeta(string Sql, Func<NpgsqlDataReader, string> BuildKey);
}
