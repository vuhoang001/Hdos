using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Events;

public sealed record FormPagePublishedDomainEvent(Guid PageId, string ModuleCode, string PageCode) : DomainEvent;
