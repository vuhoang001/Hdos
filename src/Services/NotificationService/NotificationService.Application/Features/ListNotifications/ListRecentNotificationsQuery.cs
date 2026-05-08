using Hdos.NotificationService.Application.DTOs;
using Hdos.NotificationService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.NotificationService.Application.Features.ListNotifications;

public sealed record ListRecentNotificationsQuery(int Take = 50)
    : IRequest<Result<IReadOnlyList<NotificationDto>>>;

public sealed class ListRecentNotificationsQueryHandler
    : IRequestHandler<ListRecentNotificationsQuery, Result<IReadOnlyList<NotificationDto>>>
{
    private readonly INotificationRepository _repo;

    public ListRecentNotificationsQueryHandler(INotificationRepository repo) => _repo = repo;

    public async Task<Result<IReadOnlyList<NotificationDto>>> Handle(
        ListRecentNotificationsQuery request, CancellationToken ct)
    {
        var items = await _repo.ListRecentAsync(Math.Clamp(request.Take, 1, 200), ct);
        IReadOnlyList<NotificationDto> dtos = items.Select(n => new NotificationDto(
            n.Id, n.Recipient, n.Subject, n.Body,
            n.Channel.ToString(), n.Status.ToString(),
            n.CreatedAtUtc, n.SentAtUtc)).ToList();
        return Result.Success(dtos);
    }
}
