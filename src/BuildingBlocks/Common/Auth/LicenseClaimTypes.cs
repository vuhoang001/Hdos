namespace Hdos.Common.Auth;

/// <summary>
/// Tên các JWT claim liên quan đến license.
/// Được <see cref="JwtTokenIssuer"/> nhúng vào token khi login,
/// và được đọc tại service qua <see cref="System.Security.Claims.ClaimsPrincipal"/>.
/// </summary>
/// <remarks>
/// Claim layout trong JWT payload:
/// <code>
/// "lic_plan": "pro",
/// "lic_mod":  "orders",   // multi-value — mỗi module là một claim riêng
/// "lic_mod":  "m01",
/// "lic_exp":  "2027-01-01T00:00:00.0000000Z"
/// </code>
/// </remarks>
public static class LicenseClaimTypes
{
    /// <summary>
    /// Tên plan của license. Ví dụ: <c>free</c>, <c>basic</c>, <c>pro</c>, <c>enterprise</c>.
    /// Luôn là chữ thường (lowercase). Một JWT chứa đúng một claim này.
    /// </summary>
    public const string Plan = "lic_plan";

    /// <summary>
    /// Module slug được phép dùng. Xem danh sách slug tại <see cref="HdosModules"/>.
    /// Là <b>multi-value claim</b> — một JWT có thể chứa nhiều claim cùng tên <c>lic_mod</c>,
    /// mỗi cái ứng với một module. Đọc bằng <c>User.FindAll(LicenseClaimTypes.Module)</c>.
    /// </summary>
    public const string Module = "lic_mod";

    /// <summary>
    /// Thời điểm hết hạn của <b>license</b> (ISO 8601, UTC).
    /// Khác với <c>exp</c> là thời điểm hết hạn của <b>JWT token</b>.
    /// Chỉ có mặt khi license có ngày hết hạn; license vĩnh viễn không có claim này.
    /// </summary>
    public const string ExpiresAt = "lic_exp";
}
