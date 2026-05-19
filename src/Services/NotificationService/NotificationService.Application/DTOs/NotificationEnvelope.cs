namespace Hdos.NotificationService.Application.DTOs;

/// <summary>
/// Envelope chuẩn cho mọi event push xuống client (SSE).
/// Frontend parse <c>Type</c> để route handler, không cần đoán cấu trúc payload.
/// </summary>
/// <param name="Type">Tên event, dạng snake_case. Ví dụ: <c>"notification"</c>.</param>
/// <param name="Payload">Dữ liệu nghiệp vụ của event.</param>
/// <param name="OccurredAtUtc">Thời điểm server tạo envelope (UTC).</param>
/// <param name="CorrelationId">Trace ID — giúp correlate log client ↔ server. Nullable.</param>
public sealed record NotificationEnvelope<T>(
    string Type,
    T Payload,
    DateTime OccurredAtUtc,
    string? CorrelationId = null);
