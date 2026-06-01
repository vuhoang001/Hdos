using System.Text.Json;
using Hdos.Contracts.ExternalMessages;

namespace Hdos.NotificationService.Application.DTOs;

// Message nhận từ hệ thống ngoài qua queue "be.hdos.dashboard.fe.ready"
// EventType, Source, CorrelationId... kế thừa từ ExternalMessage (CloudEvents standard)
public sealed record ProcessedToFeMessage(
    JsonElement? Payload,
    JsonElement? Data) : ExternalMessage;
