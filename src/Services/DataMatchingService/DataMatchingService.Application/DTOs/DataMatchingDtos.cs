namespace Hdos.DataMatchingService.Application.DTOs;

public sealed record SourceProfileDto(
    Guid Id,
    string SourceSystem,
    string RecordType,
    string DisplayName,
    string BusinessKeyField,
    Dictionary<string, string> Mappings);

public sealed record IngestResultDto(
    Guid Id,
    string SourceSystem,
    string RecordType,
    string? BusinessKey,
    string Status);

public sealed record IngestBatchResultDto(
    int Count,
    List<Guid> Ids);

// Dùng cho endpoint GET /dm/records — trả về canonical fields để client tự parse.
public sealed record StagingRecordDto(
    Guid Id,
    string SourceSystem,
    string RecordType,
    string? BusinessKey,
    string Status,
    string? CanonicalPayload,
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
