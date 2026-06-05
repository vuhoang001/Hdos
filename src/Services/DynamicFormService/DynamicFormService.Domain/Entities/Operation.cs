using System.Text.Json;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Entities;

// Operation = một endpoint cụ thể của Provider, đã đăng ký với pattern URL,
// schema path, danh sách param. DataSource trong screen tham chiếu Operation
// qua combined key "providerCode::operationKey".
//
// ProviderCode lưu denormalized (string) — không có FK cứng để aggregate root
// vẫn tách biệt. Constraint unique (ProviderCode, OperationKey) ở DB.
public sealed class Operation : AggregateRoot<Guid>
{
    public string          ProviderCode       { get; private set; } = null!;
    public string          OperationKey       { get; private set; } = null!; // slug, unique trong provider
    public string          DisplayName        { get; private set; } = null!;
    public string          Pattern            { get; private set; } = null!; // "/records?...{maBN}", relative so với Provider.BaseUrl
    public string?         SchemaPath         { get; private set; }          // "/sources/his-01/benh-nhan/schema"
    public string          RequiredParamsJson { get; private set; } = "[]";  // JSON array of string
    public OperationKind   Kind               { get; private set; }
    public OperationStatus Status             { get; private set; }

    private Operation()
    {
    }

    public static Operation Create(
        string              providerCode,
        string              operationKey,
        string              displayName,
        string              pattern,
        string?             schemaPath,
        IEnumerable<string> requiredParams,
        OperationKind       kind)
    {
        if (string.IsNullOrWhiteSpace(providerCode))
            throw new ArgumentException("ProviderCode is required.", nameof(providerCode));
        if (string.IsNullOrWhiteSpace(operationKey))
            throw new ArgumentException("OperationKey is required.", nameof(operationKey));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Pattern is required.", nameof(pattern));

        return new Operation
        {
            Id                 = Guid.NewGuid(),
            ProviderCode       = providerCode.Trim().ToLowerInvariant(),
            OperationKey       = operationKey.Trim().ToLowerInvariant(),
            DisplayName        = displayName.Trim(),
            Pattern            = pattern.Trim(),
            SchemaPath         = string.IsNullOrWhiteSpace(schemaPath) ? null : schemaPath.Trim(),
            RequiredParamsJson = SerializeParams(requiredParams),
            Kind               = kind,
            Status             = OperationStatus.Active
        };
    }

    public void Update(
        string              displayName,
        string              pattern,
        string?             schemaPath,
        IEnumerable<string> requiredParams,
        OperationKind       kind)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Pattern is required.", nameof(pattern));

        DisplayName        = displayName.Trim();
        Pattern            = pattern.Trim();
        SchemaPath         = string.IsNullOrWhiteSpace(schemaPath) ? null : schemaPath.Trim();
        RequiredParamsJson = SerializeParams(requiredParams);
        Kind               = kind;
        UpdatedAtUtc       = DateTime.UtcNow;
    }

    public void Activate()
    {
        Status       = OperationStatus.Active;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        Status       = OperationStatus.Inactive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public List<string> GetRequiredParams() =>
        JsonSerializer.Deserialize<List<string>>(RequiredParamsJson) ?? new();

    // Combined ref dạng "providerCode::operationKey" — dùng trong DataSource.OperationId
    public string GetCombinedRef() => $"{ProviderCode}::{OperationKey}";

    private static string SerializeParams(IEnumerable<string>? requiredParams)
    {
        var list = requiredParams?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct()
            .ToList() ?? new List<string>();
        return JsonSerializer.Serialize(list);
    }
}
