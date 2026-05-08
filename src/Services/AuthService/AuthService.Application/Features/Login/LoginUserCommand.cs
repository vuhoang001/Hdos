using FluentValidation;
using Hdos.AuthService.Application.Abstractions;
using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Repositories;
using Hdos.AuthService.Domain.ValueObjects;
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

public sealed class LoginUserCommandHandler : IRequestHandler<LoginUserCommand, Result<LoginResultDto>>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _hasher;
    private readonly IEventBus _eventBus;

    public LoginUserCommandHandler(
        IUserRepository users,
        IUnitOfWork uow,
        IPasswordHasher hasher,
        IEventBus eventBus)
    {
        _users = users;
        _uow = uow;
        _hasher = hasher;
        _eventBus = eventBus;
    }

    public async Task<Result<LoginResultDto>> Handle(LoginUserCommand request, CancellationToken ct)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure) return Result.Failure<LoginResultDto>(emailResult.Error);

        var user = await _users.GetByEmailAsync(emailResult.Value, ct);
        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
            return Result.Failure<LoginResultDto>(Error.Unauthorized("Invalid credentials"));

        user.RecordLogin();
        _users.Update(user);
        await _uow.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new UserLoggedInIntegrationEvent(user.Id, user.Email.Value, DateTime.UtcNow), ct);

        var token = $"demo-token::{user.Id}::{Guid.NewGuid():N}";
        return new LoginResultDto(user.Id, user.Email.Value, token);
    }
}
