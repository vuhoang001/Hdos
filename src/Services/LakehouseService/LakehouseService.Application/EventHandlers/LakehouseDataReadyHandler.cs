using System.Text.Json;
using Hdos.Contracts.IntegrationEvents;
using Hdos.LakehouseService.Domain.Entities;
using Hdos.LakehouseService.Domain.Errors;
using Hdos.LakehouseService.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Hdos.LakehouseService.Application.EventHandlers;

public sealed class LakehouseDataReadyHandler(
    ILakehouseSnapshotRepository repository,
    IHttpClientFactory httpClientFactory,
    ILogger<LakehouseDataReadyHandler> logger)
{
    public async Task HandleAsync(LakehouseDataReadyIntegrationEvent evt, CancellationToken ct)
    {
        string rawJson;

        if (!string.IsNullOrWhiteSpace(evt.Payload))
        {
            rawJson = evt.Payload;
        }
        else if (!string.IsNullOrWhiteSpace(evt.DownloadUrl))
        {
            rawJson = await DownloadAsync(evt.DownloadUrl, ct);
        }
        else
        {
            logger.LogWarning("LakehouseDataReady job {JobId} — {Error}", evt.JobId, LakehouseErrors.PayloadMissing.Message);
            return;
        }

        var snapshots = BuildSnapshots(evt, rawJson);
        await repository.AddRangeAsync(snapshots, ct);
        await repository.SaveChangesAsync(ct);

        logger.LogInformation(
            "LakehouseDataReady job {JobId} — {Count} snapshot(s) lưu vào namespace '{Namespace}'",
            evt.JobId, snapshots.Count, evt.Namespace);
    }

    private async Task<string> DownloadAsync(string url, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("lakehouse");
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static List<LakehouseSnapshot> BuildSnapshots(
        LakehouseDataReadyIntegrationEvent evt,
        string rawJson)
    {
        // Nếu JSON là array → mỗi element là 1 snapshot riêng
        // Nếu JSON là object → 1 snapshot duy nhất với businessKey từ event
        using var doc = JsonDocument.Parse(rawJson);

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return doc.RootElement.EnumerateArray()
                .Select(element =>
                {
                    var key = element.TryGetProperty("id", out var idProp)
                              || element.TryGetProperty("businessKey", out idProp)
                              || element.TryGetProperty("key", out idProp)
                        ? idProp.GetString() ?? evt.BusinessKey
                        : evt.BusinessKey;

                    return LakehouseSnapshot.Create(evt.Namespace, key, element.GetRawText(), evt.JobId);
                })
                .ToList();
        }

        return [LakehouseSnapshot.Create(evt.Namespace, evt.BusinessKey, rawJson, evt.JobId)];
    }
}
