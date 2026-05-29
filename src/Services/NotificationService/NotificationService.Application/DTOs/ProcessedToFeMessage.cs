namespace Hdos.NotificationService.Application.DTOs;

// Message từ bên thứ 3 publish lên exchange "processed_to_fe"
// Cập nhật fields khi biết payload chính xác
public sealed record ProcessedToFeMessage(
    string? EventType,
    string? Payload,
    object? Data);
