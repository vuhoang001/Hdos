using Hdos.Common.Auth;
using Hdos.Common.Responses;
using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Application.Features.BenhNhan;
using Hdos.M01Service.Application.Features.CapCuu;
using Hdos.M01Service.Application.Features.Dashboard;
using Hdos.M01Service.Application.Features.Forecast;
using Hdos.M01Service.Application.Features.NhanSu;
using Hdos.M01Service.Application.Features.PhongKham;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.M01Service.API.Controllers;

[ApiController]
[Route("m01")]
// [Authorize(Policy = HdosPermissions.M01Read)]
public sealed class M01Controller(ISender sender) : ControllerBase
{
    [HttpGet("dashboard/summary")]
    public async Task<IActionResult> GetDashboardSummary(CancellationToken ct)
    {
        var result = await sender.Send(new GetDashboardSummaryQuery(), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<DashboardSummaryDto>.Ok(result.Value))
            : NotFound(ApiResponse<DashboardSummaryDto>.Fail(result.Error.Code, result.Error.Message));
    }

    [HttpGet("dashboard/flow")]
    public async Task<IActionResult> GetDashboardFlow(CancellationToken ct)
    {
        var result = await sender.Send(new GetDashboardFlowQuery(), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<DashboardFlowDto>.Ok(result.Value))
            : NotFound(ApiResponse<DashboardFlowDto>.Fail(result.Error.Code, result.Error.Message));
    }

    [HttpGet("phong-kham/tai")]
    public async Task<IActionResult> GetPhongKhamTai(CancellationToken ct)
    {
        var result = await sender.Send(new GetPhongKhamTaiQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<PhongKhamDto>>.Ok(result.Value));
    }

    [HttpGet("benh-nhan")]
    public async Task<IActionResult> GetBenhNhans(
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetBenhNhansQuery(page, pageSize), ct);
        return Ok(ApiResponse<BenhNhanPageDto>.Ok(result.Value));
    }

    [HttpGet("cap-cuu")]
    public async Task<IActionResult> GetCapCuus(CancellationToken ct)
    {
        var result = await sender.Send(new GetCapCuusQuery(), ct);
        return Ok(ApiResponse<CapCuuListDto>.Ok(result.Value));
    }

    [HttpGet("nhan-su/truc")]
    public async Task<IActionResult> GetNhanSuTruc(CancellationToken ct)
    {
        var result = await sender.Send(new GetNhanSuTrucQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<NhanSuTrucDto>>.Ok(result.Value));
    }

    // [Authorize(Policy = HdosPermissions.M01Write)]
    [HttpGet("ai/forecast")]
    public async Task<IActionResult> GetAiForecast(CancellationToken ct)
    {
        var result = await sender.Send(new GetAiForecastQuery(), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AiForecastDto>.Ok(result.Value))
            : NotFound(ApiResponse<AiForecastDto>.Fail(result.Error.Code, result.Error.Message));
    }

    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "OK", service = "M01Service", at = DateTime.UtcNow });
}
