namespace Hdos.DynamicFormService.Domain.ValueObjects;

// Action: "Show" | "Hide"
// Operator: "Equals" | "NotEquals" | "Contains"
public sealed record ConditionalLogic(
    string SourceFieldKey,
    string Operator,
    string Value,
    string Action);
