using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Application.Features.WidgetCatalog;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DataMatchingService.API.Controllers;

[ApiController]
[Route("dm/widget-catalog")]
public sealed class WidgetCatalogController(IMediator mediator) : ControllerBase
{
    // GET /dm/widget-catalog
    // GET /dm/widget-catalog?category=healthcare
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? category, CancellationToken ct)
    {
        var result = await mediator.Send(new GetWidgetCatalogQuery(category), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<List<WidgetCatalogDto>>.Ok(result.Value))
            : BadRequest(ApiResponse<List<WidgetCatalogDto>>.Fail(result.Error.Code, result.Error.Message));
    }
}
