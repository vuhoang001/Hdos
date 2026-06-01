using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Hdos.AsyncGateway.API.Models;
using Hdos.Common.Auth;
using Hdos.Common.Messaging;
using Hdos.Common.Responses;
using Hdos.Contracts.IntegrationEvents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.AsyncGateway.API.Controllers;

public sealed record CreateOrderAsyncRequest(
    IReadOnlyList<OrderItemDto> Items);

[ApiController]
[Route("async/orders")]
// [Authorize(Policy = HdosPermissions.AsyncSubmit)]
public sealed class AsyncOrdersController(IEventBus eventBus) : ControllerBase
{
    /// <summary>
    /// Enqueues an order creation. Returns 202 immediately;
    /// OrderService processes the message asynchronously.
    /// CustomerId is extracted from the JWT sub claim — no need to pass it in the body.
    /// Use the returned CorrelationId to correlate logs and traces.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AsyncResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderAsyncRequest request,
        CancellationToken ct)
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(sub, out var customerId))
            return Unauthorized(ApiResponse<AsyncResponse>.Fail("Auth.InvalidToken", "Cannot resolve customer from token."));

        var correlationId = Guid.NewGuid();
        await eventBus.PublishAsync(
            new OrderCreateRequestedIntegrationEvent(
                RequestId: correlationId,
                CustomerId: customerId,
                Items: request.Items),
            ct);

        return Accepted(ApiResponse<AsyncResponse>.Ok(new AsyncResponse(correlationId)));
    }
}
