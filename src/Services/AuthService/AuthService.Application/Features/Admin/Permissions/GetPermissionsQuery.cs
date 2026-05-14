using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Repositories;
using MediatR;

namespace Hdos.AuthService.Application.Features.Admin.Permissions;

public sealed record GetPermissionsQuery : IRequest<IReadOnlyList<PermissionDto>>;

public sealed class GetPermissionsQueryHandler(IPermissionRepository perms)
    : IRequestHandler<GetPermissionsQuery, IReadOnlyList<PermissionDto>>
{
    public async Task<IReadOnlyList<PermissionDto>> Handle(GetPermissionsQuery request, CancellationToken ct)
    {
        var list = await perms.GetAllAsync(ct);
        return list.Select(p => new PermissionDto(p.Id, p.Resource, p.Action, p.Description, p.Key)).ToList();
    }
}
