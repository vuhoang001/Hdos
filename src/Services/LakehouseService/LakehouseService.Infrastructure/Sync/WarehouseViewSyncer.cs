using System.Diagnostics;
using System.Text.Json;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.LakehouseService.Domain.Entities;
using Hdos.LakehouseService.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.Sync;

/// <summary>
/// Pull data từ warehouse external (PG view), publish mỗi row thành
/// <c>RawRecordIngestRequestedIntegrationEvent</c> để <c>DataMatchingService</c> consume,
/// apply <c>SourceProfile</c> mapping → canonical record.
///
/// VIEW + cặp (SourceSystem, RecordType) được đăng ký động qua <see cref="ViewBinding"/>
/// (không hardcode). Xem doc 44 — Unified Ingest Pipeline §4, §6.
/// </summary>
public sealed class WarehouseViewSyncer(
    NpgsqlDataSource             warehouseDataSource,
    IEventBus                    eventBus,
    ISyncStateRepository         syncState,
    IViewBindingRepository       bindings,
    ILogger<WarehouseViewSyncer> logger) : IWarehouseViewSyncer
{
    public async Task<SyncResult> SyncAsync(Guid bindingId, CancellationToken ct)
    {
        var binding = await bindings.GetByIdAsync(bindingId, ct)
            ?? throw new ArgumentException($"ViewBinding '{bindingId}' không tồn tại.", nameof(bindingId));

        if (!binding.IsActive)
            throw new InvalidOperationException($"ViewBinding '{binding.ViewName}' đang Inactive.");

        return await SyncBindingAsync(binding, ct);
    }

    public async Task<List<SyncResult>> SyncAllActiveAsync(CancellationToken ct)
    {
        var actives = await bindings.ListActiveAsync(ct);
        var results = new List<SyncResult>(actives.Count);

        foreach (var b in actives)
        {
            try
            {
                results.Add(await SyncBindingAsync(b, ct));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sync failed for ViewBinding {ViewName} ({Id})", b.ViewName, b.Id);
                results.Add(new SyncResult(b.Id, b.ViewName, 0, string.Empty, TimeSpan.Zero, ex.Message));
            }
        }

        return results;
    }

    private async Task<SyncResult> SyncBindingAsync(ViewBinding b, CancellationToken ct)
    {
        var jobId = $"sync-{b.RecordType}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var sw    = Stopwatch.StartNew();
        var count = 0;

        // SELECT * giữ nguyên column name của VIEW — payload raw cho DataMatching mapping.
        var sql = $"SELECT * FROM {b.ViewName}";

        await using var conn   = await warehouseDataSource.OpenConnectionAsync(ct);
        await using var cmd    = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var fieldNames    = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var bizKeyOrdinal = SafeGetOrdinal(reader, b.BusinessKeyColumn)
            ?? throw new InvalidOperationException(
                $"Cột BusinessKey '{b.BusinessKeyColumn}' không có trong VIEW '{b.ViewName}'.");

        while (await reader.ReadAsync(ct))
        {
            var businessKey   = reader.IsDBNull(bizKeyOrdinal)
                ? string.Empty
                : reader.GetValue(bizKeyOrdinal)?.ToString() ?? string.Empty;
            var rawPayload    = BuildPayloadJson(reader, fieldNames);

            await eventBus.PublishAsync(new RawRecordIngestRequestedIntegrationEvent(
                SourceSystem:   b.SourceSystem,
                RecordType:     b.RecordType,
                BusinessKey:    businessKey,
                RawPayloadJson: rawPayload,
                SourceJobId:    jobId), ct);

            count++;
        }

        await syncState.UpsertAsync(b.ViewName, count, jobId, ct);
        sw.Stop();

        logger.LogInformation(
            "Warehouse sync {View} ({Source}/{Type}): {Count} rows in {Ms} ms (job {JobId})",
            b.ViewName, b.SourceSystem, b.RecordType, count, sw.ElapsedMilliseconds, jobId);

        return new SyncResult(b.Id, b.ViewName, count, jobId, sw.Elapsed, null);
    }

    private static int? SafeGetOrdinal(NpgsqlDataReader r, string columnName)
    {
        try { return r.GetOrdinal(columnName); }
        catch (IndexOutOfRangeException) { return null; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented          = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    private static string BuildPayloadJson(NpgsqlDataReader r, string[] fieldNames)
    {
        var dict = new Dictionary<string, object?>(r.FieldCount);
        for (var i = 0; i < r.FieldCount; i++)
            dict[fieldNames[i]] = r.IsDBNull(i) ? null : NormalizeValue(r.GetValue(i));
        return JsonSerializer.Serialize(dict, JsonOpts);
    }

    private static object? NormalizeValue(object value) => value switch
    {
        DateTime dt        => dt.ToString("o"),
        DateTimeOffset dto => dto.ToString("o"),
        DateOnly d         => d.ToString("yyyy-MM-dd"),
        TimeSpan ts        => ts.ToString(),
        Guid g             => g.ToString(),
        byte[] bytes       => Convert.ToBase64String(bytes),
        _                  => value
    };
}
