using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.AuthService.Domain.ValueObjects;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using MediatR;

namespace Hdos.AuthService.Application.Features.ValidateToken;

/// <summary>
/// Called by AuthController.ValidateToken on every nginx auth_request.
/// 1. JIT-provisions a local User profile if this Keycloak sub has never been seen.
/// 2. Resolves the user's RBAC roles + permissions from the AuthService DB.
/// Returns role/permission lists that ValidateToken writes to X-User-* response headers.
/// </summary>
public sealed record ValidateAndResolveQuery(Guid UserId, string Email, string FullName)
    : IRequest<UserContextDto>;

public sealed class ValidateAndResolveQueryHandler(
    IUserRepository users,
    IUserRoleRepository userRoles,
    IUnitOfWork uow,
    IEventBus eventBus)
    : IRequestHandler<ValidateAndResolveQuery, UserContextDto>
{
    public async Task<UserContextDto> Handle(ValidateAndResolveQuery request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(request.UserId, ct);

        if (user is null)
        {
            var emailResult = Email.Create(request.Email);
            var email = emailResult.IsSuccess ? emailResult.Value : Email.Create("unknown@unknown.com").Value;

            // Guard: Keycloak sub may have changed (user deleted & recreated) while the
            // email-unique constraint still holds — look up by email before inserting.
            var existingByEmail = await users.GetByEmailAsync(email, ct);
            if (existingByEmail is not null)
            {
                user = existingByEmail;
                user.UpdateLastSeen();
                users.Update(user);
                await uow.SaveChangesAsync(ct);
            }
            else
            {
                user = User.Provision(request.UserId, email, request.FullName);
                await users.AddAsync(user, ct);
                await uow.SaveChangesAsync(ct);

                await eventBus.PublishAsync(
                    new UserRegisteredIntegrationEvent(user.Id, user.Email.Value, user.FullName), ct);
            }
        }
        else
        {
            user.UpdateLastSeen();
            users.Update(user);
            await uow.SaveChangesAsync(ct);
        }

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
