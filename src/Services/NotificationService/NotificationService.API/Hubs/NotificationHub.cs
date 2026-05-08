using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hdos.NotificationService.API.Hubs;

/// <summary>
/// Hub realtime cho NotificationService.
///
/// Đường mount: <c>/notifications/hubs/notifications</c> (qua API Gateway giữ nguyên path).
/// Yêu cầu JWT (xem <see cref="Hdos.Common.Auth.JwtAuthExtensions"/> cho cách
/// truyền token qua query string khi negotiate WebSocket).
///
/// Server → Client events:
/// <list type="bullet">
///   <item><c>notification</c>: payload là <see cref="Hdos.NotificationService.Application.DTOs.NotificationDto"/></item>
/// </list>
///
/// Client → Server methods:
/// <list type="bullet">
///   <item><c>Ping()</c>: trả về "pong" — dùng để smoke-test connection.</item>
/// </list>
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    public Task<string> Ping() => Task.FromResult("pong");
}
