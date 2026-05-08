using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.GetUser;

public sealed record GetUserByIdQuery(Guid UserId) : IRequest<Result<UserDto>>;

public sealed class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, Result<UserDto>>
{
    private readonly IUserRepository _users;

    public GetUserByIdQueryHandler(IUserRepository users) => _users = users;

    public async Task<Result<UserDto>> Handle(GetUserByIdQuery request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null) return Result.Failure<UserDto>(Error.NotFound("User"));
        return new UserDto(user.Id, user.Email.Value, user.FullName, user.CreatedAtUtc);
    }
}
