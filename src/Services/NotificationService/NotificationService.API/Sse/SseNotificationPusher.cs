using System.Text.Json;
using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.Realtime;

namespace Hdos.NotificationService.API.Sse;

public sealed class SseNotificationPusher : INotificationPusher
{
    public const string EventName = "notification";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly SseConnectionManager _manager;
    private readonly ILogger<SseNotificationPusher> _logger;

    public SseNotificationPusher(SseConnectionManager manager, ILogger<SseNotificationPusher> logger)
    {
        _manager = manager;
        _logger  = logger;
    }

    public async Task PushToUserAsync(string userEmail, NotificationDto notification, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userEmail)) return;

        var envelope = new NotificationEnvelope<NotificationDto>(
            Type:           EventName,
            Payload:        notification,
            OccurredAtUtc:  DateTime.UtcNow);

        var data = JsonSerializer.Serialize(envelope, JsonOpts);

        await _manager.SendToUserAsync(userEmail, data, ct);

        _logger.LogInformation("Pushed SSE notification {NotificationId} → {User}",
            notification.Id, userEmail);
    }
}
