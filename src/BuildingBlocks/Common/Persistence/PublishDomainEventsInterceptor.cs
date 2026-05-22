using Hdos.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Hdos.Common.Persistence;

/// <summary>
/// EF Core SaveChanges interceptor that, *after* a successful commit, publishes
/// every <see cref="IDomainEvent"/> raised by tracked aggregates through MediatR's
/// <see cref="IPublisher"/>. Domain handlers stay in-process and run inside the
/// same DI scope as the request.
///
/// Post-save semantics: handlers cannot mutate entities and have those changes
/// persisted in the same transaction — events here represent facts that have
/// already happened. If you ever need pre-save (state-mutating) handlers, switch
/// the override below from <c>SavedChangesAsync</c> to <c>SavingChangesAsync</c>.
/// </summary>
public sealed class PublishDomainEventsInterceptor(
    IPublisher publisher,
    ILogger<PublishDomainEventsInterceptor> logger)
    : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var ctx = eventData.Context;
        if (ctx is null) return result;

        var aggregates = ctx.ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<IHasDomainEvents>()
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        if (aggregates.Count == 0) return result;

        // Snapshot then clear so re-entrant SaveChanges from within a handler
        // doesn't re-dispatch the same events.
        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
        foreach (var a in aggregates) a.ClearDomainEvents();

        logger.LogDebug("Dispatching {Count} domain event(s) after SaveChanges", events.Count);

        foreach (var @event in events)
            await publisher.Publish(@event, cancellationToken);

        return result;
    }
}