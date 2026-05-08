using FluentAssertions;
using Hdos.AuthService.Application.Features.GetUser;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.AuthService.Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace Hdos.AuthService.Tests.Application;

public sealed class GetUserByIdQueryHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private GetUserByIdQueryHandler NewHandler() => new(_users);

    [Fact]
    public async Task Handle_Found_ReturnsDto()
    {
        var user = User.Register(Email.Create("alice@hdos.io").Value, "Alice", "h");
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await NewHandler().Handle(new GetUserByIdQuery(user.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(user.Id);
        result.Value.Email.Should().Be("alice@hdos.io");
        result.Value.FullName.Should().Be("Alice");
    }

    [Fact]
    public async Task Handle_Missing_ReturnsNotFound()
    {
        _users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await NewHandler().Handle(new GetUserByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }
}
