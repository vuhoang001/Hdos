using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hdos.AuthService.Domain.Repositories;
using Hdos.Contracts.Grpc.Users;

namespace Hdos.AuthService.API.Grpc;

public sealed class UserGrpcService : UserService.UserServiceBase
{
    private readonly IUserRepository _users;
    private readonly ILogger<UserGrpcService> _logger;

    public UserGrpcService(IUserRepository users, ILogger<UserGrpcService> logger)
    {
        _users = users;
        _logger = logger;
    }

    public override async Task<UserReply> GetUserById(GetUserByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id must be a GUID"));

        var user = await _users.GetByIdAsync(id, context.CancellationToken);
        if (user is null)
        {
            _logger.LogInformation("gRPC GetUserById miss for {UserId}", id);
            throw new RpcException(new Status(StatusCode.NotFound, $"User {id} not found"));
        }

        return new UserReply
        {
            Id = user.Id.ToString(),
            Email = user.Email.Value,
            FullName = user.FullName,
            CreatedAtUtc = Timestamp.FromDateTime(DateTime.SpecifyKind(user.CreatedAtUtc, DateTimeKind.Utc))
        };
    }

    public override async Task<UserExistsReply> UserExists(UserExistsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id must be a GUID"));

        var user = await _users.GetByIdAsync(id, context.CancellationToken);
        return new UserExistsReply { Exists = user is not null };
    }
}
