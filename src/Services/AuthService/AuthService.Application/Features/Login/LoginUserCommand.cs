using FluentValidation;
using Hdos.AuthService.Application.Abstractions;
using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Repositories;
using Hdos.AuthService.Domain.ValueObjects;
using Hdos.Common.Auth;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.Login;

public sealed record LoginUserCommand(string Email, string Password) : IRequest<Result<LoginResultDto>>;

public sealed class LoginUserCommandValidator : AbstractValidator<LoginUserCommand>
{
    public LoginUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginUserCommandHandler(
    IUserRepository users,
    IUnitOfWork uow,
    IPasswordHasher hasher,
    IEventBus eventBus,
    IJwtTokenIssuer tokenIssuer)
    : IRequestHandler<LoginUserCommand, Result<LoginResultDto>>
{
    public async Task<Result<LoginResultDto>> Handle(LoginUserCommand request, CancellationToken ct)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure) return Result.Failure<LoginResultDto>(emailResult.Error);

        var user = await users.GetByEmailAsync(emailResult.Value, ct);
        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
            return Result.Failure<LoginResultDto>(Error.Unauthorized("Invalid credentials"));

        user.RecordLogin();
        users.Update(user);
        await uow.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new UserLoggedInIntegrationEvent(user.Id, user.Email.Value, DateTime.UtcNow), ct);

        var token = tokenIssuer.Issue(user.Id, user.Email.Value);
        return new LoginResultDto(user.Id, user.Email.Value, token.Token);
    }
}