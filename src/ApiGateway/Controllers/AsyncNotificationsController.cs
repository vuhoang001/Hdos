using Hdos.AsyncGateway.API.Models;
using Hdos.Common.Auth;
using Hdos.Common.Messaging;
using Hdos.Common.Responses;
using Hdos.Contracts.IntegrationEvents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.AsyncGateway.API.Controllers;

public sealed record SendNotificationAsyncRequest(
    string RecipientEmail,
    string Subject,
    string Body);

[ApiController]
[Route("async/notifications")]
// [Authorize(Policy = HdosPermissions.NotificationsSend)]
public sealed class AsyncNotificationsController(IEventBus eventBus) : ControllerBase
{
    /// <summary>
    /// Enqueues a notification. Returns 202 immediately;
    /// NotificationService processes and delivers it asynchronously.
    /// Use the returned CorrelationId to correlate logs and traces.
    /// </summary>
    [HttpPost("send")]
    [ProducesResponseType(typeof(ApiResponse<AsyncResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Send(
        [FromBody] SendNotificationAsyncRequest request,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        await eventBus.PublishAsync(
            new NotificationSendRequestedIntegrationEvent(
                RequestId: correlationId,
                RecipientEmail: request.RecipientEmail,
                Subject: request.Subject,
                Body: request.Body),
            ct);

        return Accepted(ApiResponse<AsyncResponse>.Ok(new AsyncResponse(correlationId)));
    }
}
