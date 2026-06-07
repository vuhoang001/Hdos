using Hdos.Contracts.IntegrationEvents;
using Hdos.LakehouseService.Domain.Repositories;
using Hdos.LakehouseService.Infrastructure.Sync;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Hdos.LakehouseService.Infrastructure.Consumers;

/// <summary>
/// Nhận <see cref="WarehouseRefreshedIntegrationEvent"/> từ DE pipeline → trigger
/// <see cref="IWarehouseViewSyncer"/> đối với các <c>ViewBinding</c> tương ứng.
///
/// Resolve view names → bindings: tra theo <c>ViewBinding.ViewName</c>. Nếu event không
/// truyền tên view (mảng rỗng) → sync mọi binding active.
/// </summary>
public sealed class WarehouseRefreshedConsumer(
    IWarehouseViewSyncer                syncer,
    IViewBindingRepository              bindings,
    ILogger<WarehouseRefreshedConsumer> logger)
    : IConsumer<WarehouseRefreshedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<WarehouseRefreshedIntegrationEvent> context)
    {
        var evt = context.Message;
        var ct  = context.CancellationToken;

        if (evt.ViewNames is null || evt.ViewNames.Length == 0)
        {
            var results = await syncer.SyncAllActiveAsync(ct);
            logger.LogInformation(
                "WarehouseRefreshed (all-active) from {RequestedBy}: {Success}/{Total} bindings synced",
                evt.RequestedBy ?? "(unknown)",
                results.Count(r => r.Error is null), results.Count);
            return;
        }

        var success = 0;
        var fail    = 0;

        foreach (var viewName in evt.ViewNames)
        {
            var binding = await bindings.GetByViewNameAsync(viewName, ct);
            if (binding is null)
            {
                fail++;
                logger.LogWarning("WarehouseRefreshed: no ViewBinding for view '{ViewName}'", viewName);
                continue;
            }

            try
            {
                var r = await syncer.SyncAsync(binding.Id, ct);
                success++;
                logger.LogInformation(
                    "Synced view {ViewName}: {RowCount} rows in {Ms} ms",
                    r.ViewName, r.RowCount, r.Duration.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                fail++;
                logger.LogError(ex, "Failed to sync ViewBinding {ViewName} — continue", viewName);
            }
        }

        logger.LogInformation(
            "WarehouseRefreshed from {RequestedBy} done — {Success} success, {Fail} failed",
            evt.RequestedBy ?? "(unknown)", success, fail);
    }
}
