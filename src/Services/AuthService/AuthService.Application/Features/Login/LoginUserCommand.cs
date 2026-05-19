using FluentValidation;
using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.AuthService.Domain.ValueObjects;
using Hdos.Common.Auth;
using Hdos.SharedKernel;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Hdos.AuthService.Application.Features.Login;

public sealed record LoginUserCommand(string Email, string Password)
    : IRequest<Result<LoginResultDto>>;

public sealed class LoginUserCommandValidator : AbstractValidator<LoginUserCommand>
{
    public LoginUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginUserCommandHandler(
    IUserRepository users,
    IUserRoleRepository userRoles,
    IUnitOfWork uow,
    IPasswordHasher<User> hasher,
    IJwtTokenIssuer jwt)
    : IRequestHandler<LoginUserCommand, Result<LoginResultDto>>
{
    public async Task<Result<LoginResultDto>> Handle(LoginUserCommand request, CancellationToken ct)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
            return Result.Failure<LoginResultDto>(Error.Validation("Email hoặc mật khẩu sai."));

        var user = await users.GetByEmailAsync(emailResult.Value!, ct);
        if (user is null)
            return Result.Failure<LoginResultDto>(Error.Validation("Email hoặc mật khẩu sai."));

        var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verify == PasswordVerificationResult.Failed)
            return Result.Failure<LoginResultDto>(Error.Validation("Email hoặc mật khẩu sai."));

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(hasher.HashPassword(user, request.Password));
            users.Update(user);
        }

        user.UpdateLastSeen();
        users.Update(user);

        var roles = await userRoles.GetRolesWithPermissionsAsync(user.Id, ct);
        var roleNames = roles.Select(r => r.Name).ToList();

        var token = jwt.Issue(user.Id, user.Email.Value, user.FullName, roleNames);
        await uow.SaveChangesAsync(ct);

        return new LoginResultDto(user.Id, user.Email.Value, token.Token);
    }
}
