using FluentValidation;
using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.License;

/// <summary>
/// Gán hoặc thay thế license cho một user.
/// Nếu user đã có license active, license cũ bị revoke trước khi tạo mới.
/// </summary>
/// <param name="UserId">ID của user cần gán license.</param>
/// <param name="Plan">Tên plan. Ví dụ: <c>free</c>, <c>basic</c>, <c>pro</c>, <c>enterprise</c>.</param>
/// <param name="Modules">
/// Danh sách module slug được phép dùng.
/// Xem <see cref="HdosModules"/> để biết danh sách slug hợp lệ.
/// </param>
/// <param name="ExpiresAtUtc">Ngày hết hạn (UTC). <c>null</c> = vĩnh viễn.</param>
public sealed record AssignLicenseCommand(
    Guid UserId,
    string Plan,
    IEnumerable<string> Modules,
    DateTime? ExpiresAtUtc)
    : IRequest<Result<LicenseDto>>;

/// <summary>Validator cho <see cref="AssignLicenseCommand"/>.</summary>
public sealed class AssignLicenseCommandValidator : AbstractValidator<AssignLicenseCommand>
{
    public AssignLicenseCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Plan).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Modules).NotNull();
    }
}

/// <summary>
/// Handler cho <see cref="AssignLicenseCommand"/>.
/// Revoke license cũ (nếu có) → tạo license mới → persist.
/// </summary>
public sealed class AssignLicenseCommandHandler(
    IUserRepository users,
    IUserLicenseRepository licenses,
    IUnitOfWork uow)
    : IRequestHandler<AssignLicenseCommand, Result<LicenseDto>>
{
    /// <returns>
    /// <see cref="Result{T}.IsSuccess"/> = <c>true</c> kèm <see cref="LicenseDto"/> mới tạo.<br/>
    /// <see cref="Result{T}.IsFailure"/> = <c>true</c> nếu <c>UserId</c> không tồn tại.
    /// </returns>
    public async Task<Result<LicenseDto>> Handle(AssignLicenseCommand request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure<LicenseDto>(Error.NotFound("User không tồn tại."));

        var existing = await licenses.GetActiveByUserIdAsync(request.UserId, ct);
        if (existing is not null)
        {
            existing.Revoke();
            licenses.Update(existing);
        }

        var license = UserLicense.Create(
            request.UserId, request.Plan, request.Modules, request.ExpiresAtUtc);
        await licenses.AddAsync(license, ct);
        await uow.SaveChangesAsync(ct);

        return GetUserLicenseQueryHandler.MapToDto(license);
    }
}
