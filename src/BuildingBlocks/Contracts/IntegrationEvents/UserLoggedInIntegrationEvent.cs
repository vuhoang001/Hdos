namespace Hdos.Contracts.IntegrationEvents;

public sealed record UserLoggedInIntegrationEvent(
    Guid UserId,
    string Email,
    DateTime LoggedInAtUtc) : IntegrationEvent;
