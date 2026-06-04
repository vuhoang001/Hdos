using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Application.Features.Records;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.DataMatchingService.API.Controllers;

[ApiController]
[Route("dm/records")]
// [Authorize]
public sealed class RecordsController(ISender sender) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRecord(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetRecordByIdQuery(id), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<StagingRecordDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<StagingRecordDto>.Ok(result.Value));
    }

    // Endpoint search linh hoạt — lấy danh sách record theo bộ lọc bất kỳ.
    //
    // Ví dụ sử dụng:
    //   GET /dm/records?sourceSystem=his-01&recordType=benh-nhan
    //   GET /dm/records?sourceSystem=his-01&recordType=benh-nhan&field=TenKhoa&value=Tim+Mach
    //   GET /dm/records?sourceSystem=his-01&recordType=chung-tu&from=2026-01-01&to=2026-03-31
    //   GET /dm/records?recordType=nhan-vien&field=TenKhoa&value=Noi+Tiet&limit=50
    [HttpGet]
    public async Task<IActionResult> GetRecords(
        [FromQuery] string? sourceSystem,
        [FromQuery] string? recordType,
        [FromQuery] string? field,
        [FromQuery] string? value,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var query = new GetRecordsQuery(sourceSystem, recordType, field, value, from, to, limit);
        var result = await sender.Send(query, ct);
        return Ok(ApiResponse<List<StagingRecordDto>>.Ok(result.Value));
    }
}
