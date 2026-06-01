namespace Hdos.Contracts.IntegrationEvents;

public sealed record NotificationSendRequestedIntegrationEvent(
    Guid   RequestId,
    string RecipientEmail,
    string Subject,
    string Body) : IntegrationEvent;
