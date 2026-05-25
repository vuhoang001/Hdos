using Hdos.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Hdos.Common.Persistence;

public sealed class PublishDomainEventsInterceptor(
    IPublisher publisher,
    ILogger<PublishDomainEventsInterceptor> logger)
    : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
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

        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
        foreach (var a in aggregates) a.ClearDomainEvents();

        logger.LogDebug("Dispatching {Count} domain event(s) before SaveChanges", events.Count);

        foreach (var @event in events)
            await publisher.Publish(@event, cancellationToken);

        // OutboxMessage được handler thêm vào EF tracker ở trên
        // sẽ được commit cùng transaction với business entity — không cần SaveChangesAsync lần 2

        return result;
    }
}
