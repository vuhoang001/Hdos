using System.Text.Json;
using Hdos.NotificationService.API.Sse;
using Hdos.NotificationService.Application.DTOs;

namespace Hdos.NotificationService.API.BackgroundServices;

/// <summary>
/// Chỉ dùng khi môi trường là Development.
/// Broadcast một notification giả xuống toàn bộ SSE client đang kết nối mỗi 5 giây
/// để FE có thể test mà không cần trigger event thật.
/// </summary>
public sealed class TestBroadcastService(
    SseConnectionManager manager,
    ILogger<TestBroadcastService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private int _counter;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogWarning("[DEV] TestBroadcastService started — broadcasting every 5s");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            if (manager.ConnectionCount == 0) continue;

            _counter++;
            var dto = new NotificationDto(
                Id:           Guid.NewGuid(),
                Recipient:    "broadcast",
                Subject:      $"[TEST] Ping #{_counter}",
                Body:         $"Auto-broadcast lúc {DateTime.UtcNow:HH:mm:ss} UTC — dùng để test SSE.",
                Channel:      "SSE",
                Status:       "Sent",
                CreatedAtUtc: DateTime.UtcNow,
                SentAtUtc:    DateTime.UtcNow);

            var envelope = new NotificationEnvelope<NotificationDto>(
                Type:          "notification",
                Payload:       dto,
                OccurredAtUtc: DateTime.UtcNow);

            var data = JsonSerializer.Serialize(envelope, JsonOpts);
            await manager.BroadcastAsync(data, stoppingToken);

            logger.LogDebug("[DEV] Broadcast test notification #{Counter} to {Count} connections",
                _counter, manager.ConnectionCount);
        }
    }
}
