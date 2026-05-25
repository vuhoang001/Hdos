using Hdos.Common.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hdos.NotificationService.Infrastructure.Cdc;

/// <summary>
/// Snapshot của Order row từ Debezium — ánh xạ với cột trong bảng dbo.Orders.
/// Chỉ cần map những cột mình quan tâm, phần còn lại Debezium bỏ qua.
/// </summary>
public sealed class OrderCdcRow
{
    public string? Id { get; init; }
    public string? CustomerId { get; init; }
    public string? CustomerEmail { get; init; }
    public int Status { get; init; }
}

/// <summary>
/// Consume CDC events từ Kafka topic hdos.OrderDb.dbo.Orders.
/// Ví dụ: gửi alert khi đơn hàng bị Cancel.
/// </summary>
public sealed class OrderCdcConsumer(
    IOptions<KafkaConsumerOptions> options,
    ILogger<OrderCdcConsumer> logger)
    : CdcConsumerService<OrderCdcRow>(logger)
{
    protected override KafkaConsumerOptions Options => options.Value;

    protected override Task HandleAsync(DebeziumPayload<OrderCdcRow> payload, CancellationToken ct)
    {
        switch (payload.Operation)
        {
            case CdcOperation.Created:
                logger.LogInformation(
                    "[CDC] Order created — id={Id} customer={Email}",
                    payload.After?.Id, payload.After?.CustomerEmail);
                break;

            case CdcOperation.Updated:
                var before = payload.Before?.Status;
                var after  = payload.After?.Status;
                if (before != after)
                    logger.LogInformation(
                        "[CDC] Order status changed — id={Id} {Before}→{After}",
                        payload.After?.Id, before, after);
                break;

            case CdcOperation.Deleted:
                logger.LogWarning(
                    "[CDC] Order deleted — id={Id}", payload.Before?.Id);
                break;

            case CdcOperation.Snapshot:
                logger.LogDebug(
                    "[CDC] Snapshot row — id={Id}", payload.After?.Id);
                break;
        }

        return Task.CompletedTask;
    }
}
