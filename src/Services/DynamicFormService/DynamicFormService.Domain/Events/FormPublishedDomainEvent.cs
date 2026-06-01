using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Events;

public sealed record FormPublishedDomainEvent(
    Guid   FormTemplateId,
    string ModuleCode,
    string FormKey) : DomainEvent;
