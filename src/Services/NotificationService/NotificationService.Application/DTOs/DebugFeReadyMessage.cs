using System.Text.Json;
using Hdos.Contracts.ExternalMessages;

namespace Hdos.NotificationService.Application.DTOs;

// Message nhận từ queue "be.hdos.dashboard.fe.ready.debug"
public sealed record DebugFeReadyMessage(
    JsonElement? Payload,
    JsonElement? Data) : ExternalMessage;
