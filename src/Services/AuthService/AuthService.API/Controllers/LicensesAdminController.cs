using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Application.Features.License;
using Hdos.Common.Auth;
using Hdos.Common.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.AuthService.API.Controllers;

/// <summary>
/// Quản lý license cho user — chỉ dành cho admin.
/// Tất cả endpoint yêu cầu JWT hợp lệ với role <c>admin</c>.
/// </summary>
/// <remarks>
/// Base route: <c>POST|GET|DELETE /auth/admin/licenses</c>.
/// Sau khi gán/revoke, user cần đăng nhập lại để nhận JWT mới có claims cập nhật.
/// </remarks>
[ApiController]
[Route("auth/admin/licenses")]
[Authorize(Roles = "admin")]
public sealed class LicensesAdminController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Lấy license đang active của user.
    /// </summary>
    /// <param name="userId">ID của user cần tra cứu.</param>
    /// <response code="200">License đang active.</response>
    /// <response code="404">User không có license active.</response>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LicenseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LicenseDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid userId, CancellationToken ct)
    {
        var result = await sender.Send(new GetUserLicenseQuery(userId), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse<LicenseDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<LicenseDto>.Ok(result.Value));
    }

    /// <summary>
    /// Gán hoặc thay thế license cho user.
    /// Nếu user đã có license active, license cũ tự động bị revoke trước khi tạo mới.
    /// </summary>
    /// <param name="req">Thông tin license cần gán.</param>
    /// <response code="200">License mới được tạo thành công.</response>
    /// <response code="400">Dữ liệu đầu vào không hợp lệ hoặc user không tồn tại.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<LicenseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LicenseDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Assign([FromBody] AssignLicenseRequest req, CancellationToken ct)
    {
        var result = await sender.Send(
            new AssignLicenseCommand(req.UserId, req.Plan, req.Modules, req.ExpiresAtUtc), ct);
        if (result.IsFailure)
            return BadRequest(ApiResponse<LicenseDto>.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse<LicenseDto>.Ok(result.Value));
    }

    /// <summary>
    /// Thu hồi license đang active của user.
    /// Lần đăng nhập tiếp theo của user sẽ không có claim <c>lic_mod</c> trong JWT.
    /// </summary>
    /// <param name="userId">ID của user cần thu hồi license.</param>
    /// <response code="200">Revoke thành công.</response>
    /// <response code="404">User không có license active.</response>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid userId, CancellationToken ct)
    {
        var result = await sender.Send(new RevokeLicenseCommand(userId), ct);
        if (result.IsFailure)
            return NotFound(ApiResponse.Fail(result.Error.Code, result.Error.Message));
        return Ok(ApiResponse.Ok());
    }
}

/// <summary>
/// Request body cho <c>POST /auth/admin/licenses</c>.
/// </summary>
/// <param name="UserId">ID của user cần gán license.</param>
/// <param name="Plan">Tên plan. Ví dụ: <c>free</c>, <c>basic</c>, <c>pro</c>, <c>enterprise</c>.</param>
/// <param name="Modules">
/// Danh sách module slug được phép. Xem <see cref="HdosModules"/> để biết danh sách hợp lệ.
/// </param>
/// <param name="ExpiresAtUtc">Ngày hết hạn UTC. <c>null</c> = vĩnh viễn.</param>
public sealed record AssignLicenseRequest(
    Guid UserId,
    string Plan,
    IEnumerable<string> Modules,
    DateTime? ExpiresAtUtc);
