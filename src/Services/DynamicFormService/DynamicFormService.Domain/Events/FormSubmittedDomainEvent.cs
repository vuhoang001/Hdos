using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Events;

public sealed record FormSubmittedDomainEvent(
    Guid   SubmissionId,
    Guid   FormTemplateId,
    string ModuleCode,
    string FormKey,
    Guid?  SubmittedBy) : DomainEvent;
