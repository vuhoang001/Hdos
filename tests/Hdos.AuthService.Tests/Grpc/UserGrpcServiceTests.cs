using FluentAssertions;
using Grpc.Core;
using Hdos.AuthService.API.Grpc;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.AuthService.Domain.ValueObjects;
using Hdos.Contracts.Grpc.Users;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Hdos.AuthService.Tests.Grpc;

public sealed class UserGrpcServiceTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private UserGrpcService NewService() =>
        new(_users, NullLogger<UserGrpcService>.Instance);

    private static ServerCallContext NewContext() =>
        TestServerCallContext.Create();

    private static User AUser(string email = "alice@hdos.io", string name = "Alice")
        => User.Provision(Guid.NewGuid(), Email.Create(email).Value!, name);

    [Fact]
    public async Task GetUserById_Found_MapsToReply()
    {
        var user = AUser();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var reply = await NewService().GetUserById(
            new GetUserByIdRequest { UserId = user.Id.ToString() },
            NewContext());

        reply.Id.Should().Be(user.Id.ToString());
        reply.Email.Should().Be("alice@hdos.io");
        reply.FullName.Should().Be("Alice");
        reply.CreatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetUserById_Missing_ThrowsNotFound()
    {
        _users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var act = async () => await NewService().GetUserById(
            new GetUserByIdRequest { UserId = Guid.NewGuid().ToString() },
            NewContext());

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task GetUserById_BadGuid_ThrowsInvalidArgument()
    {
        var act = async () => await NewService().GetUserById(
            new GetUserByIdRequest { UserId = "definitely-not-a-guid" },
            NewContext());

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task UserExists_ReturnsTrueWhenFound()
    {
        var user = AUser("a@b.io", "A");
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var reply = await NewService().UserExists(
            new UserExistsRequest { UserId = user.Id.ToString() },
            NewContext());

        reply.Exists.Should().BeTrue();
    }

    [Fact]
    public async Task UserExists_ReturnsFalseWhenMissing()
    {
        _users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var reply = await NewService().UserExists(
            new UserExistsRequest { UserId = Guid.NewGuid().ToString() },
            NewContext());

        reply.Exists.Should().BeFalse();
    }
}

/// <summary>
/// Minimal ServerCallContext stub so we can call grpc service methods directly
/// without spinning up an in-process gRPC channel.
/// </summary>
internal sealed class TestServerCallContext : ServerCallContext
{
    public static ServerCallContext Create() => new TestServerCallContext();

    protected override string MethodCore => "test";
    protected override string HostCore => "localhost";
    protected override string PeerCore => "test";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore { get; } = new();
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;
    protected override Metadata ResponseTrailersCore { get; } = new();
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new(string.Empty, new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
        throw new NotSupportedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
