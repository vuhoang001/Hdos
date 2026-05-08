using Hdos.AuthService.Domain.Events;
using Hdos.AuthService.Domain.ValueObjects;
using Hdos.SharedKernel;

namespace Hdos.AuthService.Domain.Entities;

public sealed class User : AggregateRoot<Guid>
{
    public Email Email { get; private set; } = default!;
    public string FullName { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public DateTime? LastLoginUtc { get; private set; }

    private User() { }

    public static User Register(Email email, string fullName, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name is required", nameof(fullName));

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = fullName.Trim(),
            PasswordHash = passwordHash
        };

        user.RaiseDomainEvent(new UserRegisteredDomainEvent(user.Id, user.Email.Value, user.FullName));
        return user;
    }

    public void RecordLogin()
    {
        LastLoginUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new UserLoggedInDomainEvent(Id, Email.Value));
    }
}
