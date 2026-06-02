using Hdos.AuthService.Domain.Entities;

namespace Hdos.AuthService.Domain.Repositories;

/// <summary>
/// Repository cho <see cref="UserLicense"/>. Không gọi <c>SaveChanges</c> trực tiếp —
/// dùng <see cref="IUnitOfWork"/> để commit.
/// </summary>
public interface IUserLicenseRepository
{
    /// <summary>
    /// Trả về license đang active (<see cref="UserLicense.IsActive"/> = <c>true</c>) của user.
    /// Nếu user có nhiều license active (tình huống bất thường), trả về cái được tạo gần nhất.
    /// Trả về <c>null</c> nếu user chưa có license nào.
    /// </summary>
    Task<UserLicense?> GetActiveByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Trả về toàn bộ lịch sử license của user (bao gồm đã revoke),
    /// sắp xếp theo <c>CreatedAtUtc</c> mới nhất trước.
    /// </summary>
    Task<IReadOnlyList<UserLicense>> GetAllByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Thêm license mới vào DbContext. Chưa commit — cần gọi <c>IUnitOfWork.SaveChangesAsync</c>.</summary>
    Task AddAsync(UserLicense license, CancellationToken ct);

    /// <summary>Đánh dấu license đã thay đổi trong DbContext. Chưa commit — cần gọi <c>IUnitOfWork.SaveChangesAsync</c>.</summary>
    void Update(UserLicense license);
}
