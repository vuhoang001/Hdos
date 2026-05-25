using FluentAssertions;
using Hdos.Common.Persistence;
using Hdos.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Hdos.BuildingBlocks.Tests.Persistence;

public sealed class PublishDomainEventsInterceptorTests
{
    public sealed record SampleDomainEvent(string Payload) : DomainEvent;

    public sealed class Widget : AggregateRoot<Guid>
    {
        public string Name { get; private set; } = default!;

        private Widget() { }

        public static Widget Create(string name)
        {
            var w = new Widget { Id = Guid.NewGuid(), Name = name };
            w.RaiseDomainEvent(new SampleDomainEvent(name));
            return w;
        }

        public void Rename(string name)
        {
            Name = name;
            RaiseDomainEvent(new SampleDomainEvent($"renamed:{name}"));
        }
    }

    // Simulates OutboxMessage — added by integration event handler during dispatch
    public sealed class SampleOutboxEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Payload { get; set; } = default!;
    }

    private sealed class WidgetDbContext : DbContext
    {
        public WidgetDbContext(DbContextOptions<WidgetDbContext> options) : base(options) { }
        public DbSet<Widget> Widgets => Set<Widget>();
        public DbSet<SampleOutboxEntry> OutboxEntries => Set<SampleOutboxEntry>();
    }

    private static (WidgetDbContext db, IPublisher publisher) NewContext()
    {
        var publisher = Substitute.For<IPublisher>();
        var interceptor = new PublishDomainEventsInterceptor(
            publisher, NullLogger<PublishDomainEventsInterceptor>.Instance);

        var options = new DbContextOptionsBuilder<WidgetDbContext>()
            .UseInMemoryDatabase($"widgets-{Guid.NewGuid()}")
            .AddInterceptors(interceptor)
            .Options;

        return (new WidgetDbContext(options), publisher);
    }

    // ── Dispatch behaviour ───────────────────────────────────────────────────

    [Fact]
    public async Task SaveChanges_NoAggregates_DoesNotPublish()
    {
        var (db, publisher) = NewContext();

        await db.SaveChangesAsync();

        await publisher.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChanges_AggregateWithoutEvents_DoesNotPublish()
    {
        var (db, publisher) = NewContext();

        var w = Widget.Create("A");
        w.ClearDomainEvents();
        db.Widgets.Add(w);
        await db.SaveChangesAsync();

        await publisher.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChanges_PublishesEachDomainEvent()
    {
        var (db, publisher) = NewContext();

        db.Widgets.Add(Widget.Create("A"));
        db.Widgets.Add(Widget.Create("B"));
        await db.SaveChangesAsync();

        await publisher.Received(2)
            .Publish(Arg.Any<SampleDomainEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChanges_ClearsEventsSoSubsequentSaveDoesNotRedispatch()
    {
        var (db, publisher) = NewContext();

        var w = Widget.Create("A");
        db.Widgets.Add(w);
        await db.SaveChangesAsync();
        publisher.ClearReceivedCalls();

        // Bypass domain method to avoid raising another event
        w.GetType()
            .GetProperty(nameof(Widget.Name))!
            .SetValue(w, "A2");
        db.Widgets.Update(w);
        await db.SaveChangesAsync();

        await publisher.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
        w.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveChanges_NewlyRaisedEventOnExistingEntity_IsPublished()
    {
        var (db, publisher) = NewContext();

        var w = Widget.Create("A");
        db.Widgets.Add(w);
        await db.SaveChangesAsync();
        publisher.ClearReceivedCalls();

        w.Rename("B");
        await db.SaveChangesAsync();

        await publisher.Received(1)
            .Publish(Arg.Is<SampleDomainEvent>(e => e.Payload == "renamed:B"),
                     Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChanges_DomainEventsCleared_AfterSave()
    {
        var (db, publisher) = NewContext();

        var w = Widget.Create("A");
        db.Widgets.Add(w);
        await db.SaveChangesAsync();

        w.DomainEvents.Should().BeEmpty();
    }

    // ── Pre-save atomicity ───────────────────────────────────────────────────
    // Proves that SavingChangesAsync (pre-save) commits OutboxMessage in the
    // SAME transaction as the business entity — no second SaveChangesAsync needed.

    [Fact]
    public async Task SaveChanges_HandlerSideEffect_PersistedWithoutSecondSave()
    {
        var (db, publisher) = NewContext();

        // Simulate integration event handler: adds OutboxEntry to EF tracker during dispatch
        publisher
            .When(p => p.Publish(Arg.Any<SampleDomainEvent>(), Arg.Any<CancellationToken>()))
            .Do(_ => db.OutboxEntries.Add(new SampleOutboxEntry { Payload = "outbox-msg" }));

        db.Widgets.Add(Widget.Create("A"));
        await db.SaveChangesAsync(); // one call — must persist both Widget and OutboxEntry

        (await db.Widgets.CountAsync()).Should().Be(1);
        (await db.OutboxEntries.CountAsync()).Should().Be(1,
            "OutboxEntry added by handler must be saved in the same SaveChangesAsync, not a second call");
    }

    [Fact]
    public async Task SaveChanges_MultipleAggregates_AllHandlerSideEffectsPersistedAtOnce()
    {
        var (db, publisher) = NewContext();

        publisher
            .When(p => p.Publish(Arg.Any<SampleDomainEvent>(), Arg.Any<CancellationToken>()))
            .Do(ci => db.OutboxEntries.Add(
                new SampleOutboxEntry { Payload = ci.Arg<SampleDomainEvent>().Payload }));

        db.Widgets.Add(Widget.Create("A"));
        db.Widgets.Add(Widget.Create("B"));
        await db.SaveChangesAsync();

        (await db.OutboxEntries.CountAsync()).Should().Be(2,
            "one OutboxEntry per domain event, all saved in the same transaction");
    }

    [Fact]
    public async Task SaveChanges_HandlerSideEffect_OnlyOnce_NotRedispatchedOnSubsequentSave()
    {
        var (db, publisher) = NewContext();

        publisher
            .When(p => p.Publish(Arg.Any<SampleDomainEvent>(), Arg.Any<CancellationToken>()))
            .Do(_ => db.OutboxEntries.Add(new SampleOutboxEntry { Payload = "msg" }));

        db.Widgets.Add(Widget.Create("A"));
        await db.SaveChangesAsync();

        // Second save (e.g. Pattern B extra save) must NOT re-dispatch
        publisher.ClearReceivedCalls();
        await db.SaveChangesAsync();

        await publisher.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
        (await db.OutboxEntries.CountAsync()).Should().Be(1, "no duplicate OutboxEntries from re-dispatch");
    }
}
