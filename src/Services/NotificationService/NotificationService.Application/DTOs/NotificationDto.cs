namespace Hdos.NotificationService.Application.DTOs;

public sealed record NotificationDto(
    Guid Id,
    string Recipient,
    string Subject,
    string Body,
    string Channel,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc);
