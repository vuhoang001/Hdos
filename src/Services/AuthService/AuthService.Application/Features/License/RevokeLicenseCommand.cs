using Hdos.AuthService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.License;

/// <summary>
/// Thu hồi license đang active của user.
/// Sau khi revoke, lần đăng nhập tiếp theo của user sẽ không có claim <c>lic_mod</c> trong JWT.
/// </summary>
/// <param name="UserId">ID của user cần thu hồi license.</param>
public sealed record RevokeLicenseCommand(Guid UserId) : IRequest<Result>;

/// <summary>Handler cho <see cref="RevokeLicenseCommand"/>.</summary>
public sealed class RevokeLicenseCommandHandler(
    IUserLicenseRepository licenses,
    IUnitOfWork uow)
    : IRequestHandler<RevokeLicenseCommand, Result>
{
    /// <returns>
    /// <see cref="Result.IsSuccess"/> = <c>true</c> nếu revoke thành công.<br/>
    /// <see cref="Result.IsFailure"/> = <c>true</c> nếu user không có license active.
    /// </returns>
    public async Task<Result> Handle(RevokeLicenseCommand request, CancellationToken ct)
    {
        var license = await licenses.GetActiveByUserIdAsync(request.UserId, ct);
        if (license is null)
            return Result.Failure(Error.NotFound("License không tồn tại."));

        license.Revoke();
        licenses.Update(license);
        await uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
