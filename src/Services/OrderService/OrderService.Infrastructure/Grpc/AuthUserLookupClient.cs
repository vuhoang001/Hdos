using Grpc.Core;
using Hdos.Contracts.Grpc.Users;
using Hdos.OrderService.Application.Abstractions;
using Hdos.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Hdos.OrderService.Infrastructure.Grpc;

/// <summary>
/// Adapter that turns the AuthService gRPC client into the
/// Application-facing <see cref="IUserLookupService"/>. NOT_FOUND becomes a
/// <see cref="Result"/> failure so handlers don't need to care about RpcException.
/// </summary>
public sealed class AuthUserLookupClient : IUserLookupService
{
    private readonly UserService.UserServiceClient _client;
    private readonly ILogger<AuthUserLookupClient> _logger;

    public AuthUserLookupClient(UserService.UserServiceClient client, ILogger<AuthUserLookupClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<Result<UserLookupDto>> GetByIdAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var reply = await _client.GetUserByIdAsync(
                new GetUserByIdRequest { UserId = userId.ToString() },
                cancellationToken: ct);

            return new UserLookupDto(Guid.Parse(reply.Id), reply.Email, reply.FullName);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return Result.Failure<UserLookupDto>(Error.NotFound("User"));
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC GetUserById failed: {Status}", ex.StatusCode);
            return Result.Failure<UserLookupDto>(
                new Error("User.GrpcError", $"AuthService gRPC error: {ex.Status.Detail}"));
        }
    }
}
