using Hdos.Common.Responses;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Operations.CreateOperation;
using Hdos.DynamicFormService.Application.Features.Operations.DeleteOperation;
using Hdos.DynamicFormService.Application.Features.Operations.GetAllOperations;
using Hdos.DynamicFormService.Application.Features.Operations.GetOperationsByProvider;
using Hdos.DynamicFormService.Application.Features.Operations.UpdateOperation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DynamicFormService.API.Controllers;

[ApiController]
[Route("forms/admin")]
// [Authorize(Policy = HdosPermissions.FormsAdmin)]
public sealed class AdminOperationsController(ISender sender) : ControllerBase
{
    // ── Operations nested under provider (CRUD) ───────────────────────────────

    /// <summary>Tạo operation cho provider.</summary>
    [HttpPost("providers/{providerCode}/operations")]
    public async Task<IActionResult> Create(
        string                       providerCode,
        [FromBody] CreateOperationBody body,
        CancellationToken            ct)
    {
        var cmd = new CreateOperationCommand(
            providerCode, body.OperationKey, body.DisplayName,
            body.Pattern, body.SchemaPath, body.RequiredParams ?? new(), body.Kind);

        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return result.Error.Code switch
            {
                "NotFound" => NotFound(ApiResponse<OperationDto>.Fail(result.Error.Code, result.Error.Message)),
                "Conflict" => Conflict(ApiResponse<OperationDto>.Fail(result.Error.Code, result.Error.Message)),
                _          => BadRequest(ApiResponse<OperationDto>.Fail(result.Error.Code, result.Error.Message))
            };
        return CreatedAtAction(null, ApiResponse<OperationDto>.Ok(result.Value));
    }

    /// <summary>Danh sách operations của một provider.</summary>
    [HttpGet("providers/{providerCode}/operations")]
    public async Task<IActionResult> ListByProvider(string providerCode, CancellationToken ct)
    {
        var result = await sender.Send(new GetOperationsByProviderQuery(providerCode), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<List<OperationDto>>.Ok(result.Value))
            : NotFound(ApiResponse<List<OperationDto>>.Fail(result.Error.Code, result.Error.Message));
    }

    /// <summary>Update operation.</summary>
    [HttpPut("providers/{providerCode}/operations/{operationKey}")]
    public async Task<IActionResult> Update(
        string                        providerCode,
        string                        operationKey,
        [FromBody] UpdateOperationBody body,
        CancellationToken             ct)
    {
        var cmd = new UpdateOperationCommand(
            providerCode, operationKey, body.DisplayName,
            body.Pattern, body.SchemaPath, body.RequiredParams ?? new(), body.Kind, body.Status);

        var result = await sender.Send(cmd, ct);
        if (result.IsFailure)
            return result.Error.Code == "NotFound"
                ? NotFound(ApiResponse<OperationDto>.Fail(result.Error.Code, result.Error.Message))
                : BadRequest(ApiResponse<OperationDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<OperationDto>.Ok(result.Value));
    }

    /// <summary>Xóa operation.</summary>
    [HttpDelete("providers/{providerCode}/operations/{operationKey}")]
    public async Task<IActionResult> Delete(
        string            providerCode,
        string            operationKey,
        CancellationToken ct)
    {
        var result = await sender.Send(new DeleteOperationCommand(providerCode, operationKey), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return NoContent();
    }

    // ── Cross-provider list (cho dropdown FE) ─────────────────────────────────

    /// <summary>Cross-provider list — FE dùng cho ProviderOperationSelect dropdown.</summary>
    [HttpGet("operations")]
    public async Task<IActionResult> ListAll([FromQuery] string? status, CancellationToken ct)
    {
        var result = await sender.Send(new GetAllOperationsQuery(status), ct);
        return Ok(ApiResponse<List<OperationDto>>.Ok(result.Value));
    }
}

public sealed record CreateOperationBody(
    string        OperationKey,
    string        DisplayName,
    string        Pattern,
    string?       SchemaPath,
    List<string>? RequiredParams,
    string        Kind);

public sealed record UpdateOperationBody(
    string        DisplayName,
    string        Pattern,
    string?       SchemaPath,
    List<string>? RequiredParams,
    string        Kind,
    string?       Status);
