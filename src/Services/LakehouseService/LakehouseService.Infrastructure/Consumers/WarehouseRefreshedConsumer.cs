using Hdos.Contracts.IntegrationEvents;
using Hdos.LakehouseService.Infrastructure.Sync;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Hdos.LakehouseService.Infrastructure.Consumers;

/// <summary>
/// Nhận <see cref="WarehouseRefreshedIntegrationEvent"/> từ DE pipeline → trigger
/// <see cref="IWarehouseViewSyncer"/> pull từng VIEW.
///
/// Pattern: thay vì poll theo lịch, DE chủ động báo "data ready" → Hdos sync ngay.
/// </summary>
public sealed class WarehouseRefreshedConsumer(
    IWarehouseViewSyncer syncer,
    ILogger<WarehouseRefreshedConsumer> logger)
    : IConsumer<WarehouseRefreshedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<WarehouseRefreshedIntegrationEvent> context)
    {
        var evt = context.Message;
        var views = evt.ViewNames is { Length: > 0 }
            ? evt.ViewNames
            : syncer.SupportedViewNames.ToArray();

        logger.LogInformation(
            "WarehouseRefreshed received from {RequestedBy} at {RefreshedAt} — syncing {Count} view(s)",
            evt.RequestedBy ?? "(unknown)", evt.RefreshedAt, views.Length);

        var ct = context.CancellationToken;
        var successCount = 0;
        var failCount    = 0;

        foreach (var viewName in views)
        {
            try
            {
                var result = await syncer.SyncAsync(viewName, ct);
                successCount++;
                logger.LogInformation(
                    "Synced view {ViewName}: {RowCount} rows in {Ms} ms",
                    result.ViewName, result.RowCount, result.Duration.TotalMilliseconds);
            }
            catch (ArgumentException ex)
            {
                failCount++;
                logger.LogWarning(ex, "Unknown view '{ViewName}' — skip", viewName);
            }
            catch (Exception ex)
            {
                failCount++;
                logger.LogError(ex, "Failed to sync view '{ViewName}' — continue with next view", viewName);
            }
        }

        logger.LogInformation(
            "WarehouseRefreshed done — {Success} success, {Fail} failed",
            successCount, failCount);
    }
}
