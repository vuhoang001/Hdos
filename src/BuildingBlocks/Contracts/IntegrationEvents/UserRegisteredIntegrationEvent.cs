namespace Hdos.Contracts.IntegrationEvents;

public sealed record UserRegisteredIntegrationEvent(
    Guid UserId,
    string Email,
    string FullName) : IntegrationEvent;
