using FluentAssertions;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Events;
using Hdos.AuthService.Domain.ValueObjects;
using Xunit;

namespace Hdos.AuthService.Tests.Domain;

public sealed class UserTests
{
    private static Email AnEmail(string s = "alice@hdos.io") => Email.Create(s).Value;

    [Fact]
    public void Register_TrimsName_AndAssignsValues()
    {
        var user = User.Register(AnEmail(), "  Alice  ", "hash");

        user.Id.Should().NotBe(Guid.Empty);
        user.Email.Value.Should().Be("alice@hdos.io");
        user.FullName.Should().Be("Alice");
        user.PasswordHash.Should().Be("hash");
        user.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        user.LastLoginUtc.Should().BeNull();
    }

    [Fact]
    public void Register_RaisesUserRegisteredDomainEvent()
    {
        var user = User.Register(AnEmail(), "Alice", "hash");

        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserRegisteredDomainEvent>()
            .Which.UserId.Should().Be(user.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_BlankName_Throws(string fullName)
    {
        var act = () => User.Register(AnEmail(), fullName, "hash");
        act.Should().Throw<ArgumentException>().WithParameterName("fullName");
    }

    [Fact]
    public void RecordLogin_SetsTimestamp_AndRaisesEvent()
    {
        var user = User.Register(AnEmail(), "Alice", "hash");
        user.ClearDomainEvents();

        user.RecordLogin();

        user.LastLoginUtc.Should().NotBeNull();
        user.LastLoginUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        user.UpdatedAtUtc.Should().NotBeNull();

        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserLoggedInDomainEvent>()
            .Which.UserId.Should().Be(user.Id);
    }
}
