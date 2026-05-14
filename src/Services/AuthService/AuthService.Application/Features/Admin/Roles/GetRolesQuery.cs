using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using MediatR;

namespace Hdos.AuthService.Application.Features.Admin.Roles;

public sealed record GetRolesQuery : IRequest<IReadOnlyList<RoleDto>>;

public sealed class GetRolesQueryHandler(IRoleRepository roles)
    : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery request, CancellationToken ct)
    {
        var list = await roles.GetAllAsync(ct);
        return list.Select(ToDto).ToList();
    }

    private static RoleDto ToDto(Role r) => new(
        r.Id,
        r.Name,
        r.Description,
        r.RolePermissions
            .Where(rp => rp.Permission is not null)
            .Select(rp => new PermissionDto(rp.Permission.Id, rp.Permission.Resource, rp.Permission.Action, rp.Permission.Description, rp.Permission.Key))
            .ToList());
}
