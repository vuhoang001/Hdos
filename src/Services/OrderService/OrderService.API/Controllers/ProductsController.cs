using Hdos.Common.Auth;
using Hdos.Common.Responses;
using Hdos.OrderService.Application.DTOs;
using Hdos.OrderService.Application.Features.Products.CreateProduct;
using Hdos.OrderService.Application.Features.Products.GetProduct;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.OrderService.API.Controllers;

[ApiController]
[Route("orders/products")]
// [Authorize]
public sealed class ProductsController(ISender sender) : ControllerBase
{
    // [Authorize(Policy = HdosPermissions.ProductsCreate)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductCommand cmd, CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<ProductDto>.Fail(result.Error.Code, result.Error.Message));
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id },
                               ApiResponse<ProductDto>.Ok(result.Value));
    }

    // [Authorize(Policy = HdosPermissions.ProductsRead)]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetProductByIdQuery(id), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<ProductDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<ProductDto>.Ok(result.Value));
    }

    // [Authorize(Policy = HdosPermissions.ProductsRead)]
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await sender.Send(new GetAllProductsQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<ProductDto>>.Ok(result.Value));
    }
}