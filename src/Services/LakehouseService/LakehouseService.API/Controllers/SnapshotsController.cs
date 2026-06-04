using Hdos.Common.Responses;
using Hdos.LakehouseService.Application.DTOs;
using Hdos.LakehouseService.Application.Features.Snapshots.GetSnapshotByKey;
using Hdos.LakehouseService.Application.Features.Snapshots.GetSnapshots;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.LakehouseService.API.Controllers;

[ApiController]
[Route("lakehouse/snapshots")]
public sealed class SnapshotsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Lấy snapshot mới nhất theo namespace + businessKey.
    /// DynamicFormService DataSource gọi endpoint này với requiredParams: ["namespace","key"].
    /// </summary>
    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest(
        [FromQuery] string @namespace,
        [FromQuery] string key,
        CancellationToken ct)
    {
        var result = await sender.Send(new GetSnapshotByKeyQuery(@namespace, key), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<LakehouseSnapshotDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<LakehouseSnapshotDto>.Ok(result.Value));
    }

    /// <summary>
    /// Lấy danh sách snapshot theo namespace (mới nhất trước).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetByNamespace(
        [FromQuery] string @namespace,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetSnapshotsQuery(@namespace, limit), ct);
        return Ok(ApiResponse<List<LakehouseSnapshotDto>>.Ok(result.Value));
    }
}
