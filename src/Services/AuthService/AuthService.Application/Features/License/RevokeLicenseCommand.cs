using Hdos.AuthService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.License;

public sealed record RevokeLicenseCommand(Guid UserId) : IRequest<Result>;

public sealed class RevokeLicenseCommandHandler(
    IUserLicenseRepository licenses,
    IUnitOfWork uow)
    : IRequestHandler<RevokeLicenseCommand, Result>
{
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
