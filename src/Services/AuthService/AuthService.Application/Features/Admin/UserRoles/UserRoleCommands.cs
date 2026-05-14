using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.Admin.UserRoles;

// ── Assign role to user ────────────────────────────────────────────────────────

public sealed record AssignRoleToUserCommand(Guid UserId, Guid RoleId) : IRequest<Result>;

public sealed class AssignRoleToUserHandler(
    IRoleRepository roles,
    IUserRoleRepository userRoles,
    IUnitOfWork uow)
    : IRequestHandler<AssignRoleToUserCommand, Result>
{
    public async Task<Result> Handle(AssignRoleToUserCommand request, CancellationToken ct)
    {
        if (!await roles.ExistsByIdAsync(request.RoleId, ct))
            return Result.Failure(Error.NotFound("Role"));

        if (await userRoles.ExistsAsync(request.UserId, request.RoleId, ct))
            return Result.Success(); // idempotent

        await userRoles.AddAsync(UserRole.Assign(request.UserId, request.RoleId), ct);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ── Revoke role from user ──────────────────────────────────────────────────────

public sealed record RevokeRoleFromUserCommand(Guid UserId, Guid RoleId) : IRequest<Result>;

public sealed class RevokeRoleFromUserHandler(IUserRoleRepository userRoles, IUnitOfWork uow)
    : IRequestHandler<RevokeRoleFromUserCommand, Result>
{
    public async Task<Result> Handle(RevokeRoleFromUserCommand request, CancellationToken ct)
    {
        var ur = await userRoles.GetAsync(request.UserId, request.RoleId, ct);
        if (ur is null) return Result.Success();

        userRoles.Remove(ur);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ── Get roles of user ──────────────────────────────────────────────────────────

public sealed record GetUserRolesQuery(Guid UserId) : IRequest<IReadOnlyList<UserRoleDto>>;

public sealed class GetUserRolesHandler(IUserRoleRepository userRoles)
    : IRequestHandler<GetUserRolesQuery, IReadOnlyList<UserRoleDto>>
{
    public async Task<IReadOnlyList<UserRoleDto>> Handle(GetUserRolesQuery request, CancellationToken ct)
    {
        var list = await userRoles.GetByUserIdAsync(request.UserId, ct);
        return list.Select(ur => new UserRoleDto(ur.UserId, ur.RoleId, ur.Role?.Name ?? string.Empty)).ToList();
    }
}
