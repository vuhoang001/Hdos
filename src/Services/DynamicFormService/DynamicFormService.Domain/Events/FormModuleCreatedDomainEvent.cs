using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Events;

public sealed record FormModuleCreatedDomainEvent(Guid ModuleId, string Code, string Name) : DomainEvent;
