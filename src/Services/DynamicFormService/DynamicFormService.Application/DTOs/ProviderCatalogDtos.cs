namespace Hdos.DynamicFormService.Application.DTOs;

// ── Provider ──────────────────────────────────────────────────────────────────

public sealed record ProviderDto(
    Guid     Id,
    string   Code,
    string   DisplayName,
    string   BaseUrl,
    string   Status,
    int      OperationCount,
    DateTime CreatedAtUtc);

// ── Operation ─────────────────────────────────────────────────────────────────

public sealed record OperationDto(
    Guid         Id,
    string       ProviderCode,
    string       OperationKey,
    string       CombinedRef,        // "providerCode::operationKey"
    string       DisplayName,
    string       Pattern,
    string?      SchemaPath,
    List<string> RequiredParams,
    string       Kind,               // "Single" | "List"
    string       Status,
    DateTime     CreatedAtUtc);
