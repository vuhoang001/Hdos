using Hdos.SharedKernel;

namespace Hdos.OrderService.Application.Abstractions;

public sealed record UserLookupDto(Guid Id, string Email, string FullName);

/// <summary>
/// Synchronous lookup of a user owned by AuthService. Implemented as a gRPC client
/// in Infrastructure; Application stays free of transport concerns so the same
/// interface could later swap to HTTP, an in-memory cache, etc.
/// </summary>
public interface IUserLookupService
{
    Task<Result<UserLookupDto>> GetByIdAsync(Guid userId, CancellationToken ct);
}
