namespace Hdos.DynamicFormService.Application.DTOs;

// ── Module ────────────────────────────────────────────────────────────────────

public sealed record FormModuleDto(
    Guid                Id,
    string              Code,
    string              Name,
    string?             Description,
    string              Status,
    int                 FormCount,
    int                 ScreenCount,
    List<FormScreenDto> Pages,
    DateTime            CreatedAtUtc);

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
    ConditionalLogicDto?      ConditionalLogic,
    DataBindingDto?           DataBinding,
    bool                      IsReadOnly);

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

// Expression binding — Approach 2 (Expression Language Engine)
public sealed record DataBindingDto(string Expression, string? DisplayFormat);

// DataSourceDto được trả về cho FE — đã resolve qua Provider/Operation catalog nếu
// DataSource ở chế độ MANAGED. FE không cần biết catalog tồn tại, chỉ dùng các field
// dưới đây để fetch trực tiếp.
//
// - BaseUrl: optional, gắn vào trước ResourcePath khi fetch. Null cho LEGACY mode.
// - Kind:    "Single" | "List" | null — FE biết cách parse response (object vs array).
// - OperationId: echo back để FE biết source nào đã ref qua catalog.
public sealed record DataSourceDto(
    string       Namespace,
    string?      ServiceId,
    string?      ResourcePath,
    List<string> RequiredParams,
    string?      SchemaPath    = null,
    string?      BaseUrl       = null,
    string?      Kind          = null,
    string?      OperationId   = null,
    Dictionary<string, string>? DefaultParams = null);

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

// ── Screen Designer ───────────────────────────────────────────────────────────

public sealed record FormScreenDto(
    Guid     Id,
    string   ModuleCode,
    string   Code,
    string   Title,
    string?  Description,
    string   Status,
    int      SortOrder,
    int      TabCount,
    DateTime CreatedAtUtc);

public sealed record FormScreenTabDto(
    Guid   Id,
    string Label,
    string Slug,
    int    SortOrder,
    bool   IsDefault,
    int    WidgetCount);

public sealed record FormScreenWidgetDto(
    Guid    Id,
    string  WidgetKey,
    string  WidgetType,
    int     GridX,
    int     GridY,
    int     GridW,
    int     GridH,
    object? Config,
    Guid?   ReferenceId);

// SDUI layout response — GET /forms/screens/{moduleCode}/{screenCode}/layout
public sealed record ScreenLayoutDto(
    Guid                     Id,
    string                   ModuleCode,
    string                   Code,
    string                   Title,
    string?                  Description,
    List<DataSourceDto>      DataSources,
    List<ScreenLayoutTabDto> Tabs,
    DateTime                 GeneratedAt);

public sealed record ScreenLayoutTabDto(
    Guid                       Id,
    string                     Label,
    string                     Slug,
    int                        SortOrder,
    bool                       IsDefault,
    List<ScreenLayoutWidgetDto> Widgets);

public sealed record ScreenLayoutWidgetDto(
    string        WidgetKey,
    string        WidgetType,
    int           GridX,
    int           GridY,
    int           GridW,
    int           GridH,
    object?       Config,
    Guid?         ReferenceId,
    FormSchemaDto? FormSchema);   // hydrated nếu WidgetType = FormSection

// Auto-generate screen+form from data source schema
public sealed record GenerateFromSourceResultDto(
    string ModuleCode,
    string ScreenCode,
    string FormKey,
    Guid   FormTemplateId,
    int    FieldsGenerated);

// Widget catalog
public sealed record WidgetCatalogItemDto(
    string       ChartType,
    string       Category,
    string       Label,
    string       Description,
    string       Icon,
    List<string> RequiredColumns,
    List<string> OptionalColumns,
    List<string> CompatibleWith,
    int          SortOrder);
