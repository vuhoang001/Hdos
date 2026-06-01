namespace Hdos.Contracts.IntegrationEvents;

public sealed record FormSubmittedIntegrationEvent(
    Guid   SubmissionId,
    Guid   FormTemplateId,
    string ModuleCode,
    string FormKey,
    Guid?  SubmittedBy) : IntegrationEvent;
