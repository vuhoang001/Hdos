namespace Hdos.DynamicFormService.Domain.ValueObjects;

// Type: "required" | "minLength" | "maxLength" | "pattern" | "min" | "max"
public sealed record ValidationRule(string Type, string Value, string ErrorMessage);
