using System.Text.Json;
using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.Realtime;

namespace Hdos.NotificationService.API.Sse;

/// <summary>
/// Implement INotificationPusher bằng SSE transport.
/// Nhận NotificationDto từ Application layer, wrap vào envelope chuẩn,
/// serialize JSON rồi đẩy xuống channel tương ứng trong SseConnectionManager.
/// </summary>
public sealed class SseNotificationPusher(SseConnectionManager manager, ILogger<SseNotificationPusher> logger)
    : INotificationPusher
{
    private const string EventName = "notification";

    // JsonSerializerDefaults.Web → dùng camelCase, phù hợp với JavaScript frontend.
    // static để tái sử dụng options, tránh allocate mới mỗi lần gọi.
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ── 1 → 1 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Push notification tới một user cụ thể (qua email).
    /// Nếu user có nhiều tab đang mở, tất cả đều nhận.
    /// Nếu user offline, không làm gì.
    /// </summary>
    public async Task PushToUserAsync(string userEmail, NotificationDto notification, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userEmail)) return;

        var data = Serialize(notification);
        await manager.SendToUserAsync(userEmail, data, ct);

        logger.LogInformation("SSE 1→1 | {NotificationId} → {User}", notification.Id, userEmail);
    }

    // ── 1 → n ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Push cùng một notification tới nhiều user (danh sách email tuỳ ý).
    /// Notification được serialize một lần duy nhất, tái sử dụng cho tất cả user.
    /// </summary>
    public async Task PushToManyAsync(IEnumerable<string> userEmails, NotificationDto notification, CancellationToken ct = default)
    {
        // Serialize trước — dù gửi cho 100 user cũng chỉ serialize 1 lần.
        var data  = Serialize(notification);
        var count = 0;

        foreach (var email in userEmails)
        {
            if (string.IsNullOrWhiteSpace(email)) continue;
            await manager.SendToUserAsync(email, data, ct);
            count++;
        }

        logger.LogInformation("SSE 1→n | {NotificationId} → {Count} users", notification.Id, count);
    }

    /// <summary>
    /// Push notification tới tất cả thành viên online của một group.
    /// Group được quản lý bởi SseConnectionManager (JoinGroup / LeaveGroup).
    ///
    /// Ví dụ groupName: "role:admin", "order:ord-123", "tenant:t1"
    /// </summary>
    public async Task PushToGroupAsync(string groupName, NotificationDto notification, CancellationToken ct = default)
    {
        var data = Serialize(notification);
        await manager.SendToGroupAsync(groupName, data, ct);

        logger.LogInformation("SSE 1→group | {NotificationId} → group [{Group}]", notification.Id, groupName);
    }

    // ── 1 → all ───────────────────────────────────────────────────────────

    /// <summary>
    /// Broadcast notification tới tất cả user đang có SSE connection.
    /// Dùng cho system-wide event: maintenance, feature announcement, v.v.
    /// </summary>
    public async Task BroadcastAsync(NotificationDto notification, CancellationToken ct = default)
    {
        var data = Serialize(notification);
        await manager.BroadcastAsync(data, ct);

        logger.LogInformation("SSE broadcast | {NotificationId} → {Total} connections",
            notification.Id, manager.ConnectionCount);
    }

    // ── 1 → all (generic event) ───────────────────────────────────────────

    /// <summary>
    /// Broadcast event với type và payload tuỳ ý xuống tất cả SSE connections.
    /// Frontend dùng <c>eventType</c> để route handler phù hợp.
    /// </summary>
    public async Task BroadcastEventAsync<T>(string eventType, T payload, CancellationToken ct = default)
    {
        var envelope = new NotificationEnvelope<T>(
            Type:          eventType,
            Payload:       payload,
            OccurredAtUtc: DateTime.UtcNow);

        var data = JsonSerializer.Serialize(envelope, JsonOpts);
        await manager.BroadcastAsync(data, ct);

        logger.LogInformation("SSE broadcast event | type={EventType} → {Total} connections",
            eventType, manager.ConnectionCount);
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private static string Serialize(NotificationDto notification)
    {
        var envelope = new NotificationEnvelope<NotificationDto>(
            Type:          EventName,
            Payload:       notification,
            OccurredAtUtc: DateTime.UtcNow);

        return JsonSerializer.Serialize(envelope, JsonOpts);
    }
}