using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.License;

/// <summary>
/// Truy vấn license đang active của user.
/// Trả về <see cref="Result{T}.IsFailure"/> nếu user không có license active.
/// </summary>
/// <param name="UserId">ID của user cần tra cứu.</param>
public sealed record GetUserLicenseQuery(Guid UserId) : IRequest<Result<LicenseDto>>;

/// <summary>Handler cho <see cref="GetUserLicenseQuery"/>.</summary>
public sealed class GetUserLicenseQueryHandler(IUserLicenseRepository licenses)
    : IRequestHandler<GetUserLicenseQuery, Result<LicenseDto>>
{
    /// <returns>
    /// <see cref="Result{T}.IsSuccess"/> = <c>true</c> kèm <see cref="LicenseDto"/>.<br/>
    /// <see cref="Result{T}.IsFailure"/> = <c>true</c> nếu không tìm thấy license active.
    /// </returns>
    public async Task<Result<LicenseDto>> Handle(GetUserLicenseQuery request, CancellationToken ct)
    {
        var license = await licenses.GetActiveByUserIdAsync(request.UserId, ct);
        if (license is null)
            return Result.Failure<LicenseDto>(Error.NotFound("License không tồn tại."));

        return MapToDto(license);
    }

    /// <summary>
    /// Map <see cref="Domain.Entities.UserLicense"/> sang <see cref="LicenseDto"/>.
    /// <c>internal</c> để <see cref="AssignLicenseCommandHandler"/> dùng chung, tránh duplicate.
    /// </summary>
    internal static LicenseDto MapToDto(Domain.Entities.UserLicense l) => new(
        l.Id, l.UserId, l.Plan, l.GetModules(),
        l.ExpiresAtUtc, l.IsActive, l.IsExpired(), l.CreatedAtUtc);
}
