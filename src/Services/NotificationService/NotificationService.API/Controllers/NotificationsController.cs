using Hdos.Common.Responses;
using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.Features.ListNotifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.NotificationService.API.Controllers;

[ApiController]
[Route("notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly ISender _sender;
    public NotificationsController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> ListRecent([FromQuery] int take = 50, CancellationToken ct = default)
    {
        var result = await _sender.Send(new ListRecentNotificationsQuery(take), ct);
        return Ok(ApiResponse<IReadOnlyList<NotificationDto>>.Ok(result.Value));
    }

    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "OK", service = "NotificationService", at = DateTime.UtcNow });
}
