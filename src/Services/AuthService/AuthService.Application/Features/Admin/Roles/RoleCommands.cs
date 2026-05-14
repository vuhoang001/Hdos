using FluentValidation;
using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.Admin.Roles;

// ── Create ────────────────────────────────────────────────────────────────────

public sealed record CreateRoleCommand(string Name, string Description) : IRequest<Result<RoleDto>>;

public sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(255);
    }
}

public sealed class CreateRoleHandler(IRoleRepository roles, IUnitOfWork uow)
    : IRequestHandler<CreateRoleCommand, Result<RoleDto>>
{
    public async Task<Result<RoleDto>> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        if (await roles.ExistsByNameAsync(request.Name, ct))
            return Result.Failure<RoleDto>(Error.Conflict($"Role '{request.Name}' already exists."));

        var role = Role.Create(request.Name, request.Description);
        await roles.AddAsync(role, ct);
        await uow.SaveChangesAsync(ct);

        return Mapper.ToDto(role);
    }
}

// ── Update ────────────────────────────────────────────────────────────────────

public sealed record UpdateRoleCommand(Guid Id, string Name, string Description) : IRequest<Result<RoleDto>>;

public sealed class UpdateRoleValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(255);
    }
}

public sealed class UpdateRoleHandler(IRoleRepository roles, IUnitOfWork uow)
    : IRequestHandler<UpdateRoleCommand, Result<RoleDto>>
{
    public async Task<Result<RoleDto>> Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        var role = await roles.GetByIdAsync(request.Id, ct);
        if (role is null) return Result.Failure<RoleDto>(Error.NotFound("Role"));

        role.Update(request.Name, request.Description);
        roles.Update(role);
        await uow.SaveChangesAsync(ct);
        return Mapper.ToDto(role);
    }
}

// ── Delete ────────────────────────────────────────────────────────────────────

public sealed record DeleteRoleCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteRoleHandler(IRoleRepository roles, IUnitOfWork uow)
    : IRequestHandler<DeleteRoleCommand, Result>
{
    public async Task<Result> Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var role = await roles.GetByIdAsync(request.Id, ct);
        if (role is null) return Result.Failure(Error.NotFound("Role"));

        roles.Delete(role);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ── Shared mapper ─────────────────────────────────────────────────────────────

file static class Mapper
{
    internal static RoleDto ToDto(Role role) => new(
        role.Id,
        role.Name,
        role.Description,
        role.RolePermissions
            .Where(rp => rp.Permission is not null)
            .Select(rp => new PermissionDto(rp.Permission!.Id, rp.Permission.Resource, rp.Permission.Action, rp.Permission.Description, rp.Permission.Key))
            .ToList());
}
