using FluentAssertions;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Events;
using Hdos.AuthService.Domain.ValueObjects;
using Xunit;

namespace Hdos.AuthService.Tests.Domain;

public sealed class UserTests
{
    private static Email AnEmail(string s = "alice@hdos.io") => Email.Create(s).Value!;

    [Fact]
    public void Provision_TrimsName_AndAssignsValues()
    {
        var id = Guid.NewGuid();
        var user = User.Provision(id, AnEmail(), "  Alice  ");

        user.Id.Should().Be(id);
        user.Email.Value.Should().Be("alice@hdos.io");
        user.FullName.Should().Be("Alice");
        user.LastSeenUtc.Should().BeNull();
        user.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Provision_RaisesUserRegisteredDomainEvent()
    {
        var user = User.Provision(Guid.NewGuid(), AnEmail(), "Alice");

        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserRegisteredDomainEvent>()
            .Which.UserId.Should().Be(user.Id);
    }

    [Fact]
    public void Provision_BlankName_FallsBackToEmail()
    {
        var user = User.Provision(Guid.NewGuid(), AnEmail("bob@hdos.io"), "   ");

        user.FullName.Should().Be("bob@hdos.io");
    }

    [Fact]
    public void UpdateLastSeen_SetsTimestamp()
    {
        var user = User.Provision(Guid.NewGuid(), AnEmail(), "Alice");

        user.UpdateLastSeen();

        user.LastSeenUtc.Should().NotBeNull();
        user.LastSeenUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        user.UpdatedAtUtc.Should().NotBeNull();
    }
}
