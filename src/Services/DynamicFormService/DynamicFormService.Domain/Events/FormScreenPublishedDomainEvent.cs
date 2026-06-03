using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Events;

public sealed record FormScreenPublishedDomainEvent(Guid ScreenId, string ModuleCode, string ScreenCode) : DomainEvent;
