using Hdos.Contracts.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Hdos.NotificationService.Infrastructure.Consumers;

/// <summary>
/// Consumer cố tình ném lỗi để quan sát cơ chế retry + dead-letter của MassTransit/RabbitMQ.
///
/// Flow khi có lỗi:
///   Lần 1 (attempt 0) → throw → MassTransit retry sau ~1s
///   Lần 2 (attempt 1) → throw → retry sau ~6s
///   Lần 3 (attempt 2) → throw → retry sau ~11s
///   Lần 4 (attempt 3) → throw → retry sau ~16s
///   Lần 5 (attempt 4) → throw → retry sau ~21s
///   Lần 6 (attempt 5) → throw → HẾT retry → message bị đẩy vào queue _error
///
/// Retry policy được cấu hình tại ServiceCollectionExtensions.AddMassTransitMessaging:
///   Exponential(retryLimit: 5, min: 1s, max: 30s, delta: 5s)
/// </summary>
public sealed class HoanggggfErrorConsumer(ILogger<HoanggggfErrorConsumer> logger)
    : IConsumer<HoanggggfIntegrationEvent>
{
    public Task Consume(ConsumeContext<HoanggggfIntegrationEvent> context)
    {
        // GetRetryAttempt() trả về số lần đã retry (0 = lần đầu tiên, chưa retry lần nào).
        var attempt = context.GetRetryAttempt();

        logger.LogWarning(
            "[HoanggggfErrorConsumer] attempt={Attempt} | MessageId={MessageId} | Name={Name} Age={Age} | Sắp throw lỗi...",
            attempt,
            context.MessageId,
            context.Message.Name,
            context.Message.Age);

        // Ném lỗi mô phỏng xử lý thất bại.
        // MassTransit bắt exception này, chờ theo Exponential backoff rồi redeliver message.
        // Sau khi hết retryLimit, message bị move sang queue tên: <queue-name>_error
        throw new InvalidOperationException(
            $"[DEMO] Lỗi cố ý tại attempt {attempt} — " +
            $"Name={context.Message.Name}, Age={context.Message.Age}");
    }
}
