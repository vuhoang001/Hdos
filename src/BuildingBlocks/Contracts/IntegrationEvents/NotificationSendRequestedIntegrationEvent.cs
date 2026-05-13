namespace Hdos.Contracts.IntegrationEvents;

public sealed record NotificationSendRequestedIntegrationEvent(
    Guid CorrelationId,
    string RecipientEmail,
    string Subject,
    string Body) : IntegrationEvent;
