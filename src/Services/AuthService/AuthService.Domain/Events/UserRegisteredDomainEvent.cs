using Hdos.SharedKernel;

namespace Hdos.AuthService.Domain.Events;

public sealed record UserRegisteredDomainEvent(Guid UserId, string Email, string FullName) : DomainEvent;

public sealed record UserLoggedInDomainEvent(Guid UserId, string Email) : DomainEvent;
