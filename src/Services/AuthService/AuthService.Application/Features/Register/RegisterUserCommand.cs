using FluentValidation;
using Hdos.AuthService.Application.Abstractions;
using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.AuthService.Domain.ValueObjects;
using Hdos.Common.Exceptions;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.AuthService.Application.Features.Register;

public sealed record RegisterUserCommand(string Email, string FullName, string Password)
    : IRequest<Result<UserDto>>;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6).MaximumLength(100);
    }
}

public sealed class RegisterUserCommandHandler
    : IRequestHandler<RegisterUserCommand, Result<UserDto>>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _hasher;
    private readonly IEventBus _eventBus;

    public RegisterUserCommandHandler(
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

    public async Task<Result<UserDto>> Handle(RegisterUserCommand request, CancellationToken ct)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
            return Result.Failure<UserDto>(emailResult.Error);

        if (await _users.ExistsByEmailAsync(emailResult.Value, ct))
            throw new ConflictException("Email is already registered.");

        var user = User.Register(emailResult.Value, request.FullName, _hasher.Hash(request.Password));
        await _users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new UserRegisteredIntegrationEvent(user.Id, user.Email.Value, user.FullName), ct);

        return new UserDto(user.Id, user.Email.Value, user.FullName, user.CreatedAtUtc);
    }
}
