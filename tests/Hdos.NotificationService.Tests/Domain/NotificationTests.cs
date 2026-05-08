using FluentAssertions;
using Hdos.NotificationService.Domain.Entities;
using Xunit;

namespace Hdos.NotificationService.Tests.Domain;

public sealed class NotificationTests
{
    [Fact]
    public void Create_DefaultsToEmail_AndPending()
    {
        var n = Notification.Create("a@b.io", "Hello", "Body");

        n.Recipient.Should().Be("a@b.io");
        n.Subject.Should().Be("Hello");
        n.Body.Should().Be("Body");
        n.Channel.Should().Be(NotificationChannel.Email);
        n.Status.Should().Be(NotificationStatus.Pending);
        n.SentAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData("", "subject")]
    [InlineData("   ", "subject")]
    [InlineData("a@b.io", "")]
    [InlineData("a@b.io", "   ")]
    public void Create_BlankRequiredFields_Throws(string recipient, string subject)
    {
        var act = () => Notification.Create(recipient, subject, "body");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_NullBody_NormalizesToEmpty()
    {
        var n = Notification.Create("a@b.io", "subj", null!);
        n.Body.Should().BeEmpty();
    }

    [Fact]
    public void MarkSent_TransitionsAndStampsTime()
    {
        var n = Notification.Create("a@b.io", "subj", "body");
        n.MarkSent();

        n.Status.Should().Be(NotificationStatus.Sent);
        n.SentAtUtc.Should().NotBeNull();
        n.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void MarkFailed_RecordsReason()
    {
        var n = Notification.Create("a@b.io", "subj", "body");
        n.MarkFailed("smtp down");

        n.Status.Should().Be(NotificationStatus.Failed);
        n.FailureReason.Should().Be("smtp down");
        n.UpdatedAtUtc.Should().NotBeNull();
    }
}
