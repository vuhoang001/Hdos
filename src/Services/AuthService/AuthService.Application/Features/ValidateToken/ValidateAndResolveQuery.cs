using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.ValidateToken;

/// <summary>
/// Gọi bởi AuthController.Validate trên mỗi nginx auth_request.
/// Resolve roles + permissions của user (đã có trong DB nhờ /auth/register) để
/// trả về header X-User-Roles / X-User-Permissions cho nginx forward sang upstream.
/// Trả failure nếu user không tồn tại (token cũ / user đã xoá).
/// </summary>
public sealed record ValidateAndResolveQuery(Guid UserId)
    : IRequest<Result<UserContextDto>>;

public sealed class ValidateAndResolveQueryHandler(
    IUserRepository users,
    IUserRoleRepository userRoles,
    IUnitOfWork uow)
    : IRequestHandler<ValidateAndResolveQuery, Result<UserContextDto>>
{
    public async Task<Result<UserContextDto>> Handle(ValidateAndResolveQuery request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure<UserContextDto>(Error.NotFound("User"));

        user.UpdateLastSeen();
        users.Update(user);
        await uow.SaveChangesAsync(ct);

        var roles = await userRoles.GetRolesWithPermissionsAsync(request.UserId, ct);
        var roleNames   = roles.Select(r => r.Name).ToList();
        var permissions = roles
            .SelectMany(r => r.RolePermissions)
            .Where(rp => rp.Permission is not null)
            .Select(rp => rp.Permission!.Key)
            .Distinct()
            .ToList();

        return new UserContextDto(roleNames, permissions);
    }
}
