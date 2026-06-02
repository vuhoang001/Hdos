using FluentValidation;
using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.License;

public sealed record GetUserLicenseQuery(Guid UserId) : IRequest<Result<LicenseDto>>;

public sealed class GetUserLicenseQueryHandler(IUserLicenseRepository licenses)
    : IRequestHandler<GetUserLicenseQuery, Result<LicenseDto>>
{
    public async Task<Result<LicenseDto>> Handle(GetUserLicenseQuery request, CancellationToken ct)
    {
        var license = await licenses.GetActiveByUserIdAsync(request.UserId, ct);
        if (license is null)
            return Result.Failure<LicenseDto>(Error.NotFound("License không tồn tại."));

        return MapToDto(license);
    }

    internal static LicenseDto MapToDto(Domain.Entities.UserLicense l) => new(
        l.Id, l.UserId, l.Plan, l.GetModules(),
        l.ExpiresAtUtc, l.IsActive, l.IsExpired(), l.CreatedAtUtc);
}
