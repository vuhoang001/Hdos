namespace Hdos.DynamicFormService.Application.DTOs;

// ── Module ────────────────────────────────────────────────────────────────────

public sealed record FormModuleDto(
    Guid   Id,
    string Code,
    string Name,
    string? Description,
    string Status,
    int    FormCount,
    DateTime CreatedAtUtc);

// ── Form Template ─────────────────────────────────────────────────────────────

public sealed record FormTemplateDto(
    Guid   Id,
    string ModuleCode,
    string Key,
    string Name,
    string? Description,
    string Status,
    int    Version,
    int    FieldCount,
    DateTime CreatedAtUtc);

// ── BDUI Schema (public endpoint) ─────────────────────────────────────────────

public sealed record FormSchemaDto(
    Guid                Id,
    string              ModuleCode,
    string              FormKey,
    string              Name,
    string?             Description,
    int                 Version,
    List<FormFieldDto>  Fields,
    FormSettingsDto     Settings);

public sealed record FormFieldDto(
    Guid                      Id,
    string                    Key,
    string                    Label,
    string                    Type,
    int                       Order,
    bool                      Required,
    string                    Width,
    string?                   Placeholder,
    string?                   HelpText,
    List<FieldOptionDto>?     Options,
    List<ValidationRuleDto>?  ValidationRules,
    ConditionalLogicDto?      ConditionalLogic);

public sealed record FormSettingsDto(
    string SubmitButtonLabel,
    string SuccessMessage,
    bool   AllowMultipleSubmissions);

public sealed record FieldOptionDto(string Label, string Value);

public sealed record ValidationRuleDto(string Type, string Value, string ErrorMessage);

public sealed record ConditionalLogicDto(
    string SourceFieldKey,
    string Operator,
    string Value,
    string Action);

// ── Submission ────────────────────────────────────────────────────────────────

public sealed record FieldAnswerInputDto(string FieldKey, string? Value);

public sealed record FormSubmissionDto(
    Guid     Id,
    string   ModuleCode,
    string   FormKey,
    int      FormVersion,
    Guid?    SubmittedBy,
    string   Status,
    DateTime SubmittedAt,
    object?  Answers);

public sealed record SubmitFormResultDto(Guid SubmissionId);
