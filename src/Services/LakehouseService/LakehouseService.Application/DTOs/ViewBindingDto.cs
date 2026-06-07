namespace Hdos.LakehouseService.Application.DTOs;

public sealed record ViewBindingDto(
    Guid     Id,
    string   ViewName,
    string   SourceSystem,
    string   RecordType,
    string   BusinessKeyColumn,
    string   UpdatedAtColumn,
    int      PollIntervalSeconds,
    bool     IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
