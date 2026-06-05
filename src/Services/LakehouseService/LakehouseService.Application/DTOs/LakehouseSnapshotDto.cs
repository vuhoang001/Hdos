namespace Hdos.LakehouseService.Application.DTOs;

public sealed record LakehouseSnapshotDto(
    Guid Id,
    string Namespace,
    string BusinessKey,
    object Payload,
    string JobId,
    DateTime ReceivedAt);

// Schema Discovery — chung contract với DataMatchingService.DataSourceSchemaDto
// để FE xử lý cùng cách bất kể nguồn từ đâu.
public sealed record DataSourceFieldDto(
    string  Key,
    string  Type,           // "string" | "number" | "date" | "boolean"
    string? Label,
    string? SourceField);

public sealed record DataSourceSchemaDto(
    string                     Namespace,
    string                     BusinessKeyField,
    List<DataSourceFieldDto>   Fields);
