using FluentAssertions;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Events;
using Hdos.AuthService.Domain.ValueObjects;
using Xunit;

namespace Hdos.AuthService.Tests.Domain;

public sealed class UserTests
{
    private const string Hash = "PBKDF2-FAKE-HASH";
    private static Email AnEmail(string s = "alice@hdos.io") => Email.Create(s).Value!;

    [Fact]
    public void Create_TrimsName_AndAssignsValues()
    {
        var user = User.Create(AnEmail(), "  Alice  ", Hash);

        user.Id.Should().NotBeEmpty();
        user.Email.Value.Should().Be("alice@hdos.io");
        user.FullName.Should().Be("Alice");
        user.PasswordHash.Should().Be(Hash);
        user.LastSeenUtc.Should().BeNull();
        user.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Create_RaisesUserRegisteredDomainEvent()
    {
        var user = User.Create(AnEmail(), "Alice", Hash);

        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserRegisteredDomainEvent>()
            .Which.UserId.Should().Be(user.Id);
    }

    [Fact]
    public void Create_BlankName_FallsBackToEmail()
    {
        var user = User.Create(AnEmail("bob@hdos.io"), "   ", Hash);

        user.FullName.Should().Be("bob@hdos.io");
    }

    [Fact]
    public void UpdateLastSeen_SetsTimestamp()
    {
        var user = User.Create(AnEmail(), "Alice", Hash);

        user.UpdateLastSeen();

        user.LastSeenUtc.Should().NotBeNull();
        user.LastSeenUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        user.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void SetPasswordHash_UpdatesHashAndTimestamp()
    {
        var user = User.Create(AnEmail(), "Alice", Hash);

        user.SetPasswordHash("NEW-HASH");

        user.PasswordHash.Should().Be("NEW-HASH");
        user.UpdatedAtUtc.Should().NotBeNull();
    }
}
