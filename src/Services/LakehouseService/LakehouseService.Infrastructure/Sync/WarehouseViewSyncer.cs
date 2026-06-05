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
                BuildKey: r => Key(
                    Date(r, 0),
                    Str(r, 1),
                    Str(r, 2))),

            ["finance_daily"] = new(
                Sql: """
                    SELECT date,
                           department_id,
                           room_id,
                           finance_bucket,
                           invoice_type_id,
                           invoice_form_id,
                           invoice_type_detail_id,
                           payment_group_id,
                           payment_source_id,
                           invoice_count,
                           distinct_encounter_count,
                           total_invoice_amount,
                           total_discount_amount
                    FROM api.finance_daily
                    """,
                BuildKey: r => Key(
                    Date(r, 0),
                    Str(r, 1),  Str(r, 2),  Str(r, 3),
                    Str(r, 4),  Str(r, 5),  Str(r, 6),
                    Str(r, 7),  Str(r, 8))),

            ["inpatient_summary_daily"] = new(
                Sql: """
                    SELECT date,
                           department_id,
                           record_type_id,
                           record_status_id,
                           medical_record_status_out_id,
                           encounter_count,
                           distinct_patient_count,
                           inpatient_encounter_count,
                           discharged_encounter_count
                    FROM api.inpatient_summary_daily
                    """,
                BuildKey: r => Key(
                    Date(r, 0),
                    Str(r, 1), Str(r, 2), Str(r, 3), Str(r, 4))),

            ["bed_occupancy"] = new(
                Sql: """
                    SELECT date,
                           department_id,
                           department_code,
                           department_name,
                           planned_bed_count,
                           actual_bed_count,
                           configured_bed_count,
                           disabled_bed_count,
                           available_bed_count,
                           occupied_bed_count,
                           occupancy_ratio
                    FROM api.bed_occupancy
                    """,
                BuildKey: r => Key(
                    Date(r, 0),
                    Str(r, 1))),

            ["clinical_pathway"] = new(
                Sql: """
                    SELECT pathway_id,
                           pathway_code,
                           pathway_name,
                           icd10_codes,
                           pathway_group_id,
                           configured_treatment_days,
                           pathway_sheet_count,
                           distinct_protocol_day_count
                    FROM api.clinical_pathway
                    """,
                BuildKey: r => Str(r, 0)),

            ["medicine_stock"] = new(
                Sql: """
                    SELECT medicine_store_id,
                           latest_log_date,
                           stock_on_hand,
                           opening_qty,
                           log_count,
                           total_approved_export
                    FROM api.medicine_stock
                    """,
                BuildKey: r => Str(r, 0)),
        };

    public IReadOnlyCollection<string> SupportedViewNames => SupportedViews.Keys.ToArray();

    // ── Key builder helpers ──────────────────────────────────────────────────
    private static string Str(NpgsqlDataReader r, int i) =>
        r.IsDBNull(i) ? "_" : (r.GetValue(i)?.ToString() ?? "_");

    private static string Date(NpgsqlDataReader r, int i) =>
        r.IsDBNull(i) ? "_" : r.GetFieldValue<DateTime>(i).ToString("yyyy-MM-dd");

    private static string Key(params string[] parts) => string.Join("|", parts);

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
