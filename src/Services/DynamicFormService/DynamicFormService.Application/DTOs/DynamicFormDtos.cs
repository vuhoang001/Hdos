using System.Text.Json.Serialization;

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

// ── Page (BDUI multi-form layout) ─────────────────────────────────────────────

public sealed record FormPageDto(
    Guid     Id,
    string   ModuleCode,
    string   Code,
    string   Title,
    string?  Description,
    string   Status,
    DateTime CreatedAtUtc);

// Layout stored as JSONB; serialized/deserialized in GetPageSchema
public sealed record FormPageLayout(List<FormPageRow> Rows);

public sealed record FormPageRow(List<FormPageComponent> Components);

// Polymorphic root — discriminator "type" matches SduiComponent pattern
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FormSectionPageComponent), "FormSection")]
[JsonDerivedType(typeof(TextBlockPageComponent),    "TextBlock")]
[JsonDerivedType(typeof(DividerPageComponent),      "Divider")]
public abstract record FormPageComponent(
    [property: JsonPropertyName("span")] int? Span);

// Nhúng một FormTemplate vào vị trí này trên trang
public sealed record FormSectionPageComponent(
    int?    Span,
    string  FormKey,           // key của FormTemplate cùng module
    string? Title = null)      // override tiêu đề form (tuỳ chọn)
    : FormPageComponent(Span);

// Khối văn bản / hướng dẫn
public sealed record TextBlockPageComponent(
    int?    Span,
    string  Content,
    string? Align = "left")    // "left" | "center" | "right"
    : FormPageComponent(Span);

// Đường phân cách
public sealed record DividerPageComponent(
    int?    Span,
    string? Label = null)
    : FormPageComponent(Span);

// Response trả về từ GET /forms/pages/{pageCode} — nhúng cả schema form
public sealed record FormPageSchemaDto(
    Guid                   Id,
    string                 ModuleCode,
    string                 Code,
    string                 Title,
    string?                Description,
    List<FormPageRowDto>   Rows,
    DateTime               GeneratedAt);

public sealed record FormPageRowDto(List<FormPageComponentDto> Components);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FormSectionPageComponentDto), "FormSection")]
[JsonDerivedType(typeof(TextBlockPageComponentDto),   "TextBlock")]
[JsonDerivedType(typeof(DividerPageComponentDto),     "Divider")]
public abstract record FormPageComponentDto(
    [property: JsonPropertyName("span")] int? Span);

// FormSection đã được hydrate với đầy đủ schema
public sealed record FormSectionPageComponentDto(
    int?          Span,
    string        FormKey,
    string?       Title,
    FormSchemaDto Schema)     // full BDUI schema của form
    : FormPageComponentDto(Span);

public sealed record TextBlockPageComponentDto(
    int?    Span,
    string  Content,
    string? Align)
    : FormPageComponentDto(Span);

public sealed record DividerPageComponentDto(
    int?    Span,
    string? Label)
    : FormPageComponentDto(Span);
