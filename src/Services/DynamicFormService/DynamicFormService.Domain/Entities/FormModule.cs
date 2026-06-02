using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Events;
using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Entities;

public sealed class FormModule : AggregateRoot<Guid>
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public ModuleStatus Status { get; private set; }

    private FormModule()
    {
    }

    public static FormModule Create(string code, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Module code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Module name is required.", nameof(name));

        var m = new FormModule
        {
            Id          = Guid.NewGuid(),
            Code        = code.Trim().ToLowerInvariant(),
            Name        = name.Trim(),
            Description = description?.Trim(),
            Status      = ModuleStatus.Active
        };
        m.RaiseDomainEvent(new FormModuleCreatedDomainEvent(m.Id, m.Code, m.Name));
        return m;
    }

    public void Update(string name, string? description)
    {
        Name         = name.Trim();
        Description  = description?.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        Status       = ModuleStatus.Inactive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Activate()
    {
        Status       = ModuleStatus.Active;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}