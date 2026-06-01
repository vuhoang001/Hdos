using System.Text.Json;

namespace Hdos.NotificationService.Application.DTOs;

// Message từ bên thứ 3 publish lên exchange "processed_to_fe"
// Dùng JsonElement? để nhận bất kỳ JSON value nào (string, object, array)
public sealed record ProcessedToFeMessage(
    string?      EventType,
    JsonElement? Payload,
    JsonElement? Data);
