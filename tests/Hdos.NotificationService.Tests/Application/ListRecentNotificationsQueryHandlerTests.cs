using FluentAssertions;
using Hdos.NotificationService.Application.Features.ListNotifications;
using Hdos.NotificationService.Domain.Entities;
using Hdos.NotificationService.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace Hdos.NotificationService.Tests.Application;

public sealed class ListRecentNotificationsQueryHandlerTests
{
    private readonly INotificationRepository _repo = Substitute.For<INotificationRepository>();
    private ListRecentNotificationsQueryHandler NewHandler() => new(_repo);

    [Fact]
    public async Task Handle_MapsToDtos()
    {
        var n = Notification.Create("a@b.io", "subj", "body");
        n.MarkSent();
        _repo.ListRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { n });

        var result = await NewHandler().Handle(new ListRecentNotificationsQuery(50), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle()
            .Which.Recipient.Should().Be("a@b.io");
    }

    [Theory]
    [InlineData(0, 1)]      // clamp lower
    [InlineData(-5, 1)]
    [InlineData(500, 200)]  // clamp upper
    [InlineData(50, 50)]
    public async Task Handle_ClampsTakeBetween1And200(int requested, int expected)
    {
        _repo.ListRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Notification>());

        await NewHandler().Handle(new ListRecentNotificationsQuery(requested), CancellationToken.None);

        await _repo.Received(1).ListRecentAsync(expected, Arg.Any<CancellationToken>());
    }
}
