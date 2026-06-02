using Hdos.SharedKernel;

namespace Hdos.AuthService.Domain.Entities;

/// <summary>
/// License gắn với một user cụ thể trong hệ thống.
/// Mỗi user chỉ có tối đa một license <see cref="IsActive"/> = <c>true</c> tại một thời điểm.
/// Khi gán license mới, license cũ bị <see cref="Revoke"/> trước.
/// </summary>
/// <remarks>
/// Danh sách module được lưu dạng CSV trong cột <see cref="ModulesCsv"/>
/// (vd: <c>"orders,m01,forms"</c>) để đơn giản hóa schema.
/// Đọc ra bằng <see cref="GetModules"/>.
/// </remarks>
public sealed class UserLicense : BaseEntity<Guid>
{
    /// <summary>ID của user sở hữu license này. FK tham chiếu bảng <c>Users</c>.</summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Tên plan. Ví dụ: <c>free</c>, <c>basic</c>, <c>pro</c>, <c>enterprise</c>.
    /// Luôn là chữ thường (lowercase). Đây là metadata — không enforce cứng business rule.
    /// </summary>
    public string Plan { get; private set; } = default!;

    /// <summary>
    /// Danh sách module slug phân cách bằng dấu phẩy.
    /// Ví dụ: <c>"orders,m01,notifications"</c>.
    /// Dùng <see cref="GetModules"/> để đọc thành list.
    /// </summary>
    public string ModulesCsv { get; private set; } = string.Empty;

    /// <summary>
    /// Thời điểm hết hạn license (UTC). <c>null</c> = license vĩnh viễn.
    /// Dùng <see cref="IsExpired"/> để kiểm tra.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; private set; }

    /// <summary>
    /// <c>true</c> nếu license đang có hiệu lực (chưa bị revoke).
    /// Một user chỉ có một license active tại một thời điểm.
    /// </summary>
    public bool IsActive { get; private set; }

    private UserLicense() { }

    /// <summary>
    /// Tạo license mới cho user. <see cref="IsActive"/> được set <c>true</c> ngay lập tức.
    /// </summary>
    /// <param name="userId">ID user sở hữu license.</param>
    /// <param name="plan">Tên plan (bắt buộc, tự động lowercase).</param>
    /// <param name="modules">
    /// Danh sách module slug. Xem <see cref="HdosModules"/> để biết danh sách hợp lệ.
    /// Tự động dedup và lowercase.
    /// </param>
    /// <param name="expiresAtUtc"><c>null</c> = vĩnh viễn.</param>
    /// <exception cref="ArgumentException">Ném nếu <paramref name="plan"/> rỗng.</exception>
    public static UserLicense Create(
        Guid userId,
        string plan,
        IEnumerable<string> modules,
        DateTime? expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(plan))
            throw new ArgumentException("Plan là bắt buộc.", nameof(plan));

        return new UserLicense
        {
            Id           = Guid.NewGuid(),
            UserId       = userId,
            Plan         = plan.Trim().ToLowerInvariant(),
            ModulesCsv   = string.Join(',', modules.Select(m => m.Trim().ToLowerInvariant()).Distinct()),
            ExpiresAtUtc = expiresAtUtc,
            IsActive     = true,
        };
    }

    /// <summary>
    /// Cập nhật plan và modules của license đang active.
    /// Không tạo license mới — dùng khi chỉ điều chỉnh nhỏ mà không cần audit trail riêng.
    /// </summary>
    public void Update(string plan, IEnumerable<string> modules, DateTime? expiresAtUtc)
    {
        Plan         = plan.Trim().ToLowerInvariant();
        ModulesCsv   = string.Join(',', modules.Select(m => m.Trim().ToLowerInvariant()).Distinct());
        ExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Vô hiệu hóa license. Sau khi revoke, user đăng nhập sẽ không có claim <c>lic_mod</c> trong JWT.
    /// Thao tác này không thể hoàn tác — nếu cần cấp lại thì tạo license mới qua <see cref="Create"/>.
    /// </summary>
    public void Revoke()
    {
        IsActive     = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Trả về danh sách module slug từ <see cref="ModulesCsv"/>.
    /// Xem <see cref="HdosModules"/> để biết danh sách slug hợp lệ.
    /// </summary>
    public IReadOnlyList<string> GetModules() =>
        ModulesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Kiểm tra license đã hết hạn chưa. License vĩnh viễn (<see cref="ExpiresAtUtc"/> = <c>null</c>)
    /// luôn trả về <c>false</c>.
    /// </summary>
    public bool IsExpired() =>
        ExpiresAtUtc.HasValue && ExpiresAtUtc.Value < DateTime.UtcNow;
}
