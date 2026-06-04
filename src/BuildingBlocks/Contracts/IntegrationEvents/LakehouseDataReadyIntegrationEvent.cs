namespace Hdos.Contracts.IntegrationEvents;

public sealed record LakehouseDataReadyIntegrationEvent(
    string JobId,
    string Namespace,
    string BusinessKey,
    string? Payload,
    string? DownloadUrl,
    int TotalRecords,
    DateTime ProcessedAt) : IntegrationEvent;
