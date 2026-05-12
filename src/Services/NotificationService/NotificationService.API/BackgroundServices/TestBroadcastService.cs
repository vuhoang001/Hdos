using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.API.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Hdos.NotificationService.API.BackgroundServices;

/// <summary>
/// Chỉ dùng khi môi trường là Development.
/// Broadcast một notification giả xuống toàn bộ client đang kết nối mỗi 5 giây
/// để FE có thể test SignalR mà không cần trigger event thật.
/// </summary>
public sealed class TestBroadcastService(
    IHubContext<NotificationHub> hub,
    ILogger<TestBroadcastService> logger) : BackgroundService
{
    private int _counter;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogWarning("[DEV] TestBroadcastService started — broadcasting every 5s");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            _counter++;
            var dto = new NotificationDto(
                Id: Guid.NewGuid(),
                Recipient: "test@hdos.dev",
                Subject: $"[TEST] Ping #{_counter}",
                Body: $"Auto-broadcast lúc {DateTime.UtcNow:HH:mm:ss} UTC — dùng để test SignalR.",
                Channel: "SignalR",
                Status: "Sent",
                CreatedAtUtc: DateTime.UtcNow,
                SentAtUtc: DateTime.UtcNow);

            var envelope = new SignalREnvelope<NotificationDto>(
                Type: SignalRNotificationPusher.EventName,
                Payload: dto,
                OccurredAtUtc: DateTime.UtcNow);

            await hub.Clients.All.SendAsync(
                SignalRNotificationPusher.EventName, envelope, stoppingToken);

            logger.LogDebug("[DEV] Broadcast test notification #{Counter}", _counter);
        }
    }
}
