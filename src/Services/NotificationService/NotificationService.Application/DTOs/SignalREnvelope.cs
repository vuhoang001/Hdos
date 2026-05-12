namespace Hdos.NotificationService.Application.DTOs;

/// <summary>
/// Envelope chuẩn cho mọi message SignalR push xuống client.
/// Mọi event gửi qua hub đều phải bọc trong struct này — frontend
/// parse <c>Type</c> để route handler, không cần đoán cấu trúc payload.
/// </summary>
/// <typeparam name="T">Kiểu payload cụ thể (ví dụ: <see cref="NotificationDto"/>).</typeparam>
/// <param name="Type">
/// Tên event, dạng snake_case. Ví dụ: <c>"notification"</c>, <c>"order.updated"</c>.
/// Frontend switch-case theo field này.
/// </param>
/// <param name="Payload">Dữ liệu nghiệp vụ của event.</param>
/// <param name="OccurredAtUtc">Thời điểm server tạo envelope (UTC).</param>
/// <param name="CorrelationId">
/// Trace/correlation ID từ request gốc — giúp correlate log client ↔ server.
/// Nullable; client bỏ qua nếu null.
/// </param>
public sealed record SignalREnvelope<T>(
    string Type,
    T Payload,
    DateTime OccurredAtUtc,
    string? CorrelationId = null);
