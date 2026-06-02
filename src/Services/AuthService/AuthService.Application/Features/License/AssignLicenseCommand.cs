using FluentValidation;
using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.License;

public sealed record AssignLicenseCommand(
    Guid UserId,
    string Plan,
    IEnumerable<string> Modules,
    DateTime? ExpiresAtUtc)
    : IRequest<Result<LicenseDto>>;

public sealed class AssignLicenseCommandValidator : AbstractValidator<AssignLicenseCommand>
{
    public AssignLicenseCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Plan).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Modules).NotNull();
    }
}

public sealed class AssignLicenseCommandHandler(
    IUserRepository users,
    IUserLicenseRepository licenses,
    IUnitOfWork uow)
    : IRequestHandler<AssignLicenseCommand, Result<LicenseDto>>
{
    public async Task<Result<LicenseDto>> Handle(AssignLicenseCommand request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure<LicenseDto>(Error.NotFound("User không tồn tại."));

        // Revoke license cũ nếu có
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
