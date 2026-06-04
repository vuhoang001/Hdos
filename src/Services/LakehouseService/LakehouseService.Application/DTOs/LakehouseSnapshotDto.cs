namespace Hdos.LakehouseService.Application.DTOs;

public sealed record LakehouseSnapshotDto(
    Guid Id,
    string Namespace,
    string BusinessKey,
    object Payload,
    string JobId,
    DateTime ReceivedAt);
