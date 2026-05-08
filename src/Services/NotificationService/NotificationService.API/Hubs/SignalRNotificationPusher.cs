using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Hdos.NotificationService.API.Hubs;

public sealed class SignalRNotificationPusher : INotificationPusher
{
    public const string EventName = "notification";

    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<SignalRNotificationPusher> _logger;

    public SignalRNotificationPusher(
        IHubContext<NotificationHub> hub,
        ILogger<SignalRNotificationPusher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task PushToUserAsync(string userEmail, NotificationDto notification, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userEmail)) return;

        await _hub.Clients.User(userEmail).SendAsync(EventName, notification, ct);
        _logger.LogInformation("Pushed SignalR notification {NotificationId} → {User}",
            notification.Id, userEmail);
    }
}
