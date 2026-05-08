using Hdos.NotificationService.Domain.Entities;

namespace Hdos.NotificationService.Application.DTOs;

internal static class NotificationMappingExtensions
{
    public static NotificationDto ToDto(this Notification n) => new(
        n.Id, n.Recipient, n.Subject, n.Body,
        n.Channel.ToString(), n.Status.ToString(),
        n.CreatedAtUtc, n.SentAtUtc);
}
