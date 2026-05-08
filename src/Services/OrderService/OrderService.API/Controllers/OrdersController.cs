using Hdos.Common.Responses;
using Hdos.OrderService.Application.DTOs;
using Hdos.OrderService.Application.Features.CreateOrder;
using Hdos.OrderService.Application.Features.GetOrder;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.OrderService.API.Controllers;

[ApiController]
[Route("orders")]
[Authorize]
public sealed class OrdersController : ControllerBase
{
    private readonly ISender _sender;

    public OrdersController(ISender sender) => _sender = sender;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderCommand cmd, CancellationToken ct)
    {
        var result = await _sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<OrderDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id },
            ApiResponse<OrderDto>.Ok(result.Value));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _sender.Send(new GetOrderByIdQuery(id), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<OrderDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<OrderDto>.Ok(result.Value));
    }

    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "OK", service = "OrderService", at = DateTime.UtcNow });
}
