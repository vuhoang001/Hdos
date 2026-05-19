using System.Text.Json;
using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.Realtime;

namespace Hdos.NotificationService.API.Sse;

public sealed class SseNotificationPusher(SseConnectionManager manager, ILogger<SseNotificationPusher> logger)
    : INotificationPusher
{
    private const string EventName = "notification";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task PushToUserAsync(string userEmail, NotificationDto notification, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userEmail)) return;

        var envelope = new NotificationEnvelope<NotificationDto>(
            Type:           EventName,
            Payload:        notification,
            OccurredAtUtc:  DateTime.UtcNow);

        var data = JsonSerializer.Serialize(envelope, JsonOpts);

        await manager.SendToUserAsync(userEmail, data, ct);

        logger.LogInformation("Pushed SSE notification {NotificationId} → {User}",
            notification.Id, userEmail);
    }
}
