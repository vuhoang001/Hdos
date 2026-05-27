namespace Hdos.DataMatchingService.Application.DTOs;

public sealed record SourceProfileDto(
    Guid Id,
    string SourceSystem,
    string DisplayName,
    string BusinessKeyField,
    Dictionary<string, string> Mappings);

public sealed record IngestResultDto(
    Guid Id,
    string SourceSystem,
    string? BusinessKey,
    string Status);

public sealed record IngestBatchResultDto(
    int Count,
    List<Guid> Ids);

public sealed record StagingRecordDto(
    Guid Id,
    string SourceSystem,
    string? BusinessKey,
    string Status,
    DateTime ReceivedAt,
    DateTime? ProcessedAt);

public sealed record ReportColumnDto(
    string Key,
    string Label,
    string Type);

public sealed record ReportRowDto(
    Dictionary<string, object?> Data);

public sealed record ReportDto(
    string ReportCode,
    string ReportName,
    DateTime GeneratedAt,
    List<ReportColumnDto> Columns,
    List<ReportRowDto> Rows,
    Dictionary<string, object?> Summary);
