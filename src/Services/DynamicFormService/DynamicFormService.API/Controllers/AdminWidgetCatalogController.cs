using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.WidgetCatalog.GetWidgetCatalog;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

[ApiController]
[Route("forms/admin/widget-catalog")]
// [Authorize(Policy = HdosPermissions.FormsAdmin)]
public sealed class AdminWidgetCatalogController(ISender sender) : ControllerBase
{
    /// <summary>Danh sách widget templates cho Screen Designer (filter optional theo category).</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? category, CancellationToken ct)
    {
        var result = await sender.Send(new GetWidgetCatalogQuery(category), ct);
        return Ok(ApiResponse<List<WidgetCatalogItemDto>>.Ok(result.Value));
    }
}
