using Hdos.Common.Messaging;
using Hdos.Common.Responses;
using Hdos.Contracts.IntegrationEvents;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.AsyncGateway.API.Controllers;

[ApiController]
[Route("async/test")]
public sealed class TestController(IEventBus eventBus) : ControllerBase
{
    /// <summary>
    /// Publishes a TestIntegrationEvent to verify the MassTransit pipeline end-to-end.
    /// NotificationService.TestConsumer sẽ nhận và log "Test integrations" ra console.
    /// Không yêu cầu auth — chỉ dùng cho development/testing.
    /// </summary>
    [HttpPost("publish")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Publish(CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        var @event = new TestIntegrationEvent(
            CorrelationId: correlationId,
            CustomerId: Guid.NewGuid(),
            Items: [new OrderItemDto("Test-Product", 1, 9.99m)]);

        await eventBus.PublishAsync(@event, ct);

        return Accepted(ApiResponse<object>.Ok(new
        {
            @event.EventId,
            correlationId,
            message = "TestIntegrationEvent published. Check NotificationService logs."
        }));
    }
}
