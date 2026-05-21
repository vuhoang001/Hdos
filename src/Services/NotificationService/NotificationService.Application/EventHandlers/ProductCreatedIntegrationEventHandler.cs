using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Application.Realtime;

namespace Hdos.NotificationService.Application.EventHandlers;

public class ProductCreatedIntegrationEventHandler(INotificationPusher pusher)
    : IIntegrationEventHandler<ProductCreatedIntegrationEvent>
{
    public async Task HandleAsync(ProductCreatedIntegrationEvent @event, CancellationToken ct)
    {
        var notification = new NotificationDto(
            Id: Guid.NewGuid(),
            Recipient: "all",
            Subject: $"Tạo thành công Product: {@event.ProductId}!",
            Body: $"Tạo mới sản phẩm: Name={@event.ProductName}, Price={@event.ProductPrice}",
            Channel: "SSE",
            Status: "Sent",
            CreatedAtUtc: DateTime.UtcNow,
            SentAtUtc: DateTime.UtcNow);
        await pusher.BroadcastAsync(notification, ct);
    }
}