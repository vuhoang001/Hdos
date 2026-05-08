using Hdos.SharedKernel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hdos.Common.Persistence;

/// <summary>
/// Catch-all handler — logs every domain event that flows through the dispatcher.
/// Registered as an open generic in DI so a single class subscribes to every
/// concrete domain event type.
/// </summary>
public sealed class LoggingDomainEventHandler<TEvent> : INotificationHandler<TEvent>
    where TEvent : IDomainEvent
{
    private readonly ILogger<LoggingDomainEventHandler<TEvent>> _logger;

    public LoggingDomainEventHandler(ILogger<LoggingDomainEventHandler<TEvent>> logger)
        => _logger = logger;

    public Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[DomainEvent] {EventType} (Id={EventId}, At={OccurredOnUtc:O})",
            typeof(TEvent).Name,
            notification.EventId,
            notification.OccurredOnUtc);
        return Task.CompletedTask;
    }
}
