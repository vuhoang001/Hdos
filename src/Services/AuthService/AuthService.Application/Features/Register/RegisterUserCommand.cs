using FluentValidation;
using Hdos.AuthService.Application.DTOs;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.AuthService.Domain.ValueObjects;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.SharedKernel;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Hdos.AuthService.Application.Features.Register;

public sealed record RegisterUserCommand(string Email, string Password, string FullName)
    : IRequest<Result<UserDto>>;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(120);
    }
}

public sealed class RegisterUserCommandHandler(
    IUserRepository users,
    IUnitOfWork uow,
    IEventBus eventBus,
    IPasswordHasher<User> hasher)
    : IRequestHandler<RegisterUserCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(RegisterUserCommand request, CancellationToken ct)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure) return Result.Failure<UserDto>(emailResult.Error);
        var email = emailResult.Value!;

        if (await users.ExistsByEmailAsync(email, ct))
            return Result.Failure<UserDto>(Error.Conflict("Email đã được sử dụng."));

        var user = User.Create(email, request.FullName, passwordHash: "placeholder");
        user.SetPasswordHash(hasher.HashPassword(user, request.Password));

        await users.AddAsync(user, ct);
        await uow.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new UserRegisteredIntegrationEvent(user.Id, user.Email.Value, user.FullName), ct);

        return new UserDto(user.Id, user.Email.Value, user.FullName, user.CreatedAtUtc);
    }
}
