using Hdos.NotificationService.Application.DTOs;

namespace Hdos.NotificationService.Application.Realtime;

/// <summary>
/// Cổng (port) để Application bắn notification realtime ra ngoài
/// mà không phụ thuộc trực tiếp vào SignalR. Implement nằm ở tầng API.
/// </summary>
public interface INotificationPusher
{
    Task PushToUserAsync(string userEmail, NotificationDto notification, CancellationToken ct = default);
}
