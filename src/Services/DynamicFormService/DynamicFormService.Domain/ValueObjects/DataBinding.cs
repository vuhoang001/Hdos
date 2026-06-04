namespace Hdos.DynamicFormService.Domain.ValueObjects;

// Expression: Mustache-style, e.g. "{{sources.patient.fullName}}"
// DisplayFormat: frontend hint, e.g. "date:DD/MM/YYYY" | "currency:VND" | null
public sealed record DataBinding(
    string  Expression,
    string? DisplayFormat);
