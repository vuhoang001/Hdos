namespace Hdos.Common.Auth;

/// <summary>
/// Phát hành JWT access token cho AuthService.
/// Chỉ AuthService gọi interface này (trong <c>LoginUserCommandHandler</c>).
/// Các service khác chỉ validate token — không cần interface này.
/// </summary>
public interface IJwtTokenIssuer
{
    /// <summary>
    /// Tạo và ký JWT chứa identity + permission + license claims.
    /// Token dùng HS256 với secret từ cấu hình <c>Jwt:Secret</c>.
    /// </summary>
    /// <param name="userId">ID của user — trở thành claim <c>sub</c>.</param>
    /// <param name="email">Email — trở thành claim <c>email</c> và <c>preferred_username</c>.</param>
    /// <param name="fullName">Tên đầy đủ — trở thành claim <c>name</c>.</param>
    /// <param name="roles">Danh sách role — mỗi role là một claim <c>roles</c> riêng.</param>
    /// <param name="permissions">
    /// Danh sách permission key (vd: <c>orders:read</c>) —
    /// mỗi permission là một claim <c>permission</c> riêng.
    /// Xem <see cref="HdosPermissions"/> để biết danh sách hợp lệ.
    /// </param>
    /// <param name="license">
    /// Thông tin license được nhúng vào JWT. Nếu <c>null</c> thì không có claim license nào.
    /// Xem <see cref="LicenseInfo"/>.
    /// </param>
    /// <returns>JWT string và thời điểm hết hạn.</returns>
    JwtTokenResult Issue(
        Guid userId,
        string email,
        string fullName,
        IEnumerable<string> roles,
        IEnumerable<string> permissions,
        LicenseInfo? license = null);
}

/// <summary>
/// Thông tin license được nhúng vào JWT khi user đăng nhập thành công.
/// Được đọc bởi <see cref="JwtTokenIssuer"/> để tạo các claims <c>lic_plan</c>,
/// <c>lic_mod</c>, <c>lic_exp</c>. Xem <see cref="LicenseClaimTypes"/>.
/// </summary>
/// <param name="Plan">
/// Tên plan của license. Ví dụ: <c>free</c>, <c>basic</c>, <c>pro</c>, <c>enterprise</c>.
/// Chỉ là metadata — logic giới hạn thực sự nằm ở <paramref name="Modules"/>.
/// </param>
/// <param name="Modules">
/// Danh sách module slug được phép dùng. Xem <see cref="HdosModules"/> để biết danh sách hợp lệ.
/// Mỗi slug trở thành một claim <c>lic_mod</c> riêng trong JWT.
/// </param>
/// <param name="ExpiresAtUtc">
/// Thời điểm hết hạn license (UTC). <c>null</c> = license vĩnh viễn,
/// claim <c>lic_exp</c> sẽ không được thêm vào JWT.
/// </param>
public sealed record LicenseInfo(
    string Plan,
    IEnumerable<string> Modules,
    DateTime? ExpiresAtUtc);

/// <summary>
/// Kết quả trả về sau khi phát hành JWT thành công.
/// </summary>
/// <param name="Token">JWT string đã ký, sẵn sàng trả về client.</param>
/// <param name="ExpiresAtUtc">Thời điểm token hết hạn (UTC), khớp với claim <c>exp</c>.</param>
public sealed record JwtTokenResult(string Token, DateTime ExpiresAtUtc);
