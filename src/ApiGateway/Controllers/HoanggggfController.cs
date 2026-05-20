using Hdos.AsyncGateway.API.Models;
using Hdos.Common.Messaging;
using Hdos.Common.Responses;
using Hdos.Contracts.IntegrationEvents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.AsyncGateway.API.Controllers;

[ApiController]
[Route("async/hoanggggf")]
[Authorize]
public class HoanggggfController(IEventBus eventBus) : ControllerBase
{
    public async Task<IActionResult> Send([FromBody] HoanggggfIntegrationEvent @event)
    {
        var correlationId = Guid.NewGuid();

        await eventBus.PublishAsync(new HoanggggfIntegrationEvent("Trần Vũ Hoàng", 23));

        return Accepted(ApiResponse<AsyncResponse>.Ok(new AsyncResponse(correlationId)));
    }
}