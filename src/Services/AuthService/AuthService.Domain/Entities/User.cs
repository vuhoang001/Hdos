using Hdos.AuthService.Domain.Events;
using Hdos.AuthService.Domain.ValueObjects;
using Hdos.SharedKernel;

namespace Hdos.AuthService.Domain.Entities;

/// <summary>
/// User của hệ thống (custom auth, HS256 JWT do AuthService phát).
/// PasswordHash do PasswordHasher<User> sinh (PBKDF2 built-in của ASP.NET Core).
/// </summary>
public sealed class User : AggregateRoot<Guid>
{
    public Email Email { get; private set; } = default!;
    public string FullName { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public DateTime? LastSeenUtc { get; private set; }

    private User() { }

    /// <summary>Tạo user mới qua /auth/register.</summary>
    public static User Create(Email email, string fullName, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("PasswordHash là bắt buộc.", nameof(passwordHash));
        if (string.IsNullOrWhiteSpace(fullName))
            fullName = email.Value;

        var user = new User
        {
            Id           = Guid.NewGuid(),
            Email        = email,
            FullName     = fullName.Trim(),
            PasswordHash = passwordHash,
        };

        user.RaiseDomainEvent(new UserRegisteredDomainEvent(user.Id, user.Email.Value, user.FullName));
        return user;
    }

    public void SetPasswordHash(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash))
            throw new ArgumentException("PasswordHash là bắt buộc.", nameof(newHash));
        PasswordHash = newHash;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void UpdateLastSeen()
    {
        LastSeenUtc  = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
