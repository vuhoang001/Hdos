using FluentValidation;
using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.Admin.Permissions;

// ── Create ────────────────────────────────────────────────────────────────────

public sealed record CreatePermissionCommand(string Resource, string Action, string Description)
    : IRequest<Result<PermissionDto>>;

public sealed class CreatePermissionValidator : AbstractValidator<CreatePermissionCommand>
{
    public CreatePermissionValidator()
    {
        RuleFor(x => x.Resource).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Action).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(255);
    }
}

public sealed class CreatePermissionHandler(IPermissionRepository perms, IUnitOfWork uow)
    : IRequestHandler<CreatePermissionCommand, Result<PermissionDto>>
{
    public async Task<Result<PermissionDto>> Handle(CreatePermissionCommand request, CancellationToken ct)
    {
        if (await perms.ExistsAsync(request.Resource, request.Action, ct))
            return Result.Failure<PermissionDto>(Error.Conflict(
                $"Permission '{request.Resource}:{request.Action}' already exists."));

        var p = Permission.Create(request.Resource, request.Action, request.Description);
        await perms.AddAsync(p, ct);
        await uow.SaveChangesAsync(ct);

        return LocalHelper.ToDto(p);
    }
}

// ── Delete ────────────────────────────────────────────────────────────────────

public sealed record DeletePermissionCommand(Guid Id) : IRequest<Result>;

public sealed class DeletePermissionHandler(IPermissionRepository perms, IUnitOfWork uow)
    : IRequestHandler<DeletePermissionCommand, Result>
{
    public async Task<Result> Handle(DeletePermissionCommand request, CancellationToken ct)
    {
        var p = await perms.GetByIdAsync(request.Id, ct);
        if (p is null) return Result.Failure(Error.NotFound("Permission"));

        perms.Delete(p);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ── Assign permission to role ─────────────────────────────────────────────────

public sealed record AssignPermissionToRoleCommand(Guid RoleId, Guid PermissionId) : IRequest<Result>;

public sealed class AssignPermissionHandler(
    IRoleRepository roles,
    IPermissionRepository perms,
    IUnitOfWork uow)
    : IRequestHandler<AssignPermissionToRoleCommand, Result>
{
    public async Task<Result> Handle(AssignPermissionToRoleCommand request, CancellationToken ct)
    {
        var role = await roles.GetByIdWithPermissionsAsync(request.RoleId, ct);
        if (role is null) return Result.Failure(Error.NotFound("Role"));

        if (role.RolePermissions.Any(rp => rp.PermissionId == request.PermissionId))
            return Result.Success(); // idempotent

        if (!await perms.ExistsByIdAsync(request.PermissionId, ct))
            return Result.Failure(Error.NotFound("Permission"));

        role.RolePermissions.Add(RolePermission.Create(request.RoleId, request.PermissionId));
        roles.Update(role);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ── Revoke permission from role ───────────────────────────────────────────────

public sealed record RevokePermissionFromRoleCommand(Guid RoleId, Guid PermissionId) : IRequest<Result>;

public sealed class RevokePermissionHandler(IRoleRepository roles, IUnitOfWork uow)
    : IRequestHandler<RevokePermissionFromRoleCommand, Result>
{
    public async Task<Result> Handle(RevokePermissionFromRoleCommand request, CancellationToken ct)
    {
        var role = await roles.GetByIdWithPermissionsAsync(request.RoleId, ct);
        if (role is null) return Result.Failure(Error.NotFound("Role"));

        var link = role.RolePermissions.FirstOrDefault(rp => rp.PermissionId == request.PermissionId);
        if (link is null) return Result.Success(); // already removed

        role.RolePermissions.Remove(link);
        roles.Update(role);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

file static class LocalHelper
{
    internal static PermissionDto ToDto(Permission p) =>
        new(p.Id, p.Resource, p.Action, p.Description, p.Key);
}
