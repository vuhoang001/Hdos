using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Hdos.NotificationService.API.Sse;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.NotificationService.API.Controllers;

[ApiController]
[Route("notifications/sse")]
// [Authorize]
public sealed class SseController(SseConnectionManager manager, ILogger<SseController> logger)
    : ControllerBase
{
    /// <summary>
    /// SSE stream — giữ connection mở, server push event xuống khi có notification.
    /// Token truyền qua ?access_token= vì EventSource không support Authorization header.
    /// </summary>
    [HttpGet]
    public async Task Stream(CancellationToken ct)
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
            ?? User.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // tắt nginx buffering

        // Gửi comment ngay để browser biết connection đã mở
        await Response.WriteAsync(": connected\n\n", ct);
        await Response.Body.FlushAsync(ct);

        var (connectionId, channel) = manager.Add(userId);
        logger.LogInformation("SSE connected: {User} ({ConnectionId}), total={Total}",
                              userId, connectionId, manager.ConnectionCount);

        try
        {
            await foreach (var data in channel.Reader.ReadAllAsync(ct))
            {
                await Response.WriteAsync($"event: notification\ndata: {data}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            /* client đóng tab — bình thường */
        }
        finally
        {
            manager.Remove(userId, connectionId);
            logger.LogInformation("SSE disconnected: {User} ({ConnectionId}), total={Total}",
                                  userId, connectionId, manager.ConnectionCount);
        }
    }
}