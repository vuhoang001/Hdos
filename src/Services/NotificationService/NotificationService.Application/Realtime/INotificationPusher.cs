using Hdos.NotificationService.Application.DTOs;

namespace Hdos.NotificationService.Application.Realtime;

/// <summary>
/// Cổng (port) để Application bắn notification realtime ra ngoài
/// mà không phụ thuộc trực tiếp vào SSE, SignalR hay bất kỳ transport cụ thể nào.
/// Implement nằm ở tầng API (SseNotificationPusher).
///
/// Ba kiểu gửi:
///   1 → 1   : PushToUserAsync   — push tới một email cụ thể
///   1 → n   : PushToManyAsync   — push tới danh sách email
///             PushToGroupAsync  — push tới tất cả thành viên trong group
///   1 → all : BroadcastAsync    — push tới toàn bộ user đang kết nối
/// </summary>
public interface INotificationPusher
{
    /// <summary>1 → 1: Push tới một user cụ thể. Nếu offline thì bỏ qua.</summary>
    Task PushToUserAsync(string userEmail, NotificationDto notification, CancellationToken ct = default);

    /// <summary>1 → n: Push tới danh sách user. User nào offline thì bỏ qua.</summary>
    Task PushToManyAsync(IEnumerable<string> userEmails, NotificationDto notification, CancellationToken ct = default);

    /// <summary>
    /// 1 → n (group): Push tới tất cả thành viên đang online trong group.
    /// Group là tập hợp userId được quản lý bởi transport layer (SseConnectionManager).
    /// </summary>
    Task PushToGroupAsync(string groupName, NotificationDto notification, CancellationToken ct = default);

    /// <summary>1 → all: Broadcast tới tất cả user đang kết nối.</summary>
    Task BroadcastAsync(NotificationDto notification, CancellationToken ct = default);
}
