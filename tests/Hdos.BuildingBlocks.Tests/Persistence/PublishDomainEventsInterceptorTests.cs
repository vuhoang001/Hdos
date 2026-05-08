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

    private sealed class WidgetDbContext : DbContext
    {
        public WidgetDbContext(DbContextOptions<WidgetDbContext> options) : base(options) { }
        public DbSet<Widget> Widgets => Set<Widget>();
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

        // Create then clear so the entity is tracked but has no pending events
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

        db.Widgets.Add(Widget.Create("A")); // raises 1 SampleDomainEvent
        db.Widgets.Add(Widget.Create("B")); // raises 1 SampleDomainEvent
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

        // No new RaiseDomainEvent — only an update.
        w.GetType()
            .GetProperty(nameof(Widget.Name))!
            .SetValue(w, "A2"); // bypass domain method to avoid raising another event
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
}
