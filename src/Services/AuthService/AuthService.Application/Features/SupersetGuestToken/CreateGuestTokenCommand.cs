using FluentValidation;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.SupersetGuestToken;

/// <summary>
/// Phát hành Superset guest token cho FE nhúng dashboard qua iframe.
/// Token có TTL ngắn (~5 phút, set trong superset_config.py GUEST_TOKEN_JWT_EXP_SECONDS).
/// FE phải refresh token trước khi expired bằng cách gọi lại endpoint này.
/// </summary>
/// <param name="DashboardId">Embedded UUID của dashboard (set trong Superset UI khi enable embed).</param>
/// <param name="Username">Username hiển thị trong audit log Superset.</param>
/// <param name="FirstName">First name (dùng cho UI Superset).</param>
/// <param name="LastName">Last name (dùng cho UI Superset).</param>
public sealed record CreateGuestTokenCommand(
    Guid DashboardId,
    string Username,
    string FirstName,
    string LastName)
    : IRequest<Result<GuestTokenDto>>;

public sealed record GuestTokenDto(string Token);

public sealed class CreateGuestTokenCommandValidator : AbstractValidator<CreateGuestTokenCommand>
{
    public CreateGuestTokenCommandValidator()
    {
        RuleFor(x => x.DashboardId).NotEmpty();
        RuleFor(x => x.Username).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
    }
}

public sealed class CreateGuestTokenCommandHandler(ISupersetAdminClient client)
    : IRequestHandler<CreateGuestTokenCommand, Result<GuestTokenDto>>
{
    public async Task<Result<GuestTokenDto>> Handle(CreateGuestTokenCommand request, CancellationToken ct)
    {
        try
        {
            var token = await client.IssueGuestTokenAsync(
                request.DashboardId,
                request.Username,
                request.FirstName,
                request.LastName,
                ct);
            return new GuestTokenDto(token);
        }
        catch (SupersetApiException ex)
        {
            return Result.Failure<GuestTokenDto>(Error.Validation($"Superset guest token failed: {ex.Message}"));
        }
    }
}

/// <summary>Abstraction để Handler không phụ thuộc trực tiếp vào HttpClient (Clean Architecture).</summary>
public interface ISupersetAdminClient
{
    Task<string> IssueGuestTokenAsync(
        Guid dashboardId, string username, string firstName, string lastName, CancellationToken ct);
}

/// <summary>Lỗi khi gọi Superset Admin API (login/guest_token).</summary>
public sealed class SupersetApiException(string message, Exception? inner = null)
    : Exception(message, inner);
