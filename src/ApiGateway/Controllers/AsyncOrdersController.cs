using Hdos.AsyncGateway.API.Models;
using Hdos.Common.Messaging;
using Hdos.Common.Responses;
using Hdos.Contracts.IntegrationEvents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.AsyncGateway.API.Controllers;

public sealed record CreateOrderAsyncRequest(
    Guid CustomerId,
    IReadOnlyList<OrderItemDto> Items);

[ApiController]
[Route("async/orders")]
[Authorize]
public sealed class AsyncOrdersController : ControllerBase
{
    private readonly IEventBus _eventBus;

    public AsyncOrdersController(IEventBus eventBus) => _eventBus = eventBus;

    /// <summary>
    /// Enqueues an order creation. Returns 202 immediately;
    /// OrderService processes the message asynchronously.
    /// Use the returned CorrelationId to correlate logs and traces.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AsyncResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderAsyncRequest request,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        await _eventBus.PublishAsync(
            new OrderCreateRequestedIntegrationEvent(
                CorrelationId: correlationId,
                CustomerId: request.CustomerId,
                Items: request.Items),
            ct);

        return Accepted(ApiResponse<AsyncResponse>.Ok(new AsyncResponse(correlationId)));
    }
}
