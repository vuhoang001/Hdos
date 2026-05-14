using Hdos.AuthService.Domain.Events;
using Hdos.AuthService.Domain.ValueObjects;
using Hdos.SharedKernel;

namespace Hdos.AuthService.Domain.Entities;

/// <summary>
/// Profile store keyed by Keycloak subject (sub claim).
/// Created lazily (JIT) the first time a Keycloak token is validated via /auth/validate.
/// Authentication is fully delegated to Keycloak — no password stored here.
/// </summary>
public sealed class User : AggregateRoot<Guid>
{
    public Email Email { get; private set; } = default!;
    public string FullName { get; private set; } = default!;
    public DateTime? LastSeenUtc { get; private set; }

    private User() { }

    /// <summary>Creates a local profile for a Keycloak user on first token validation.</summary>
    public static User Provision(Guid keycloakId, Email email, string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            fullName = email.Value;

        var user = new User
        {
            Id       = keycloakId,
            Email    = email,
            FullName = fullName.Trim(),
        };

        user.RaiseDomainEvent(new UserRegisteredDomainEvent(user.Id, user.Email.Value, user.FullName));
        return user;
    }

    public void UpdateLastSeen()
    {
        LastSeenUtc  = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
