using FluentAssertions;
using Hdos.SharedKernel;
using Xunit;

namespace Hdos.BuildingBlocks.Tests.SharedKernel;

public sealed class AggregateRootTests
{
    private sealed record TestEvent(string Name) : DomainEvent;

    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public TestAggregate() => Id = Guid.NewGuid();
        public void Do(string name) => RaiseDomainEvent(new TestEvent(name));
    }

    [Fact]
    public void RaiseDomainEvent_AppendsToCollection()
    {
        var agg = new TestAggregate();

        agg.Do("first");
        agg.Do("second");

        agg.DomainEvents.Should().HaveCount(2);
        agg.DomainEvents.Cast<TestEvent>()
            .Select(e => e.Name)
            .Should().ContainInOrder("first", "second");
    }

    [Fact]
    public void ClearDomainEvents_EmptiesCollection()
    {
        var agg = new TestAggregate();
        agg.Do("x");

        agg.ClearDomainEvents();

        agg.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ImplementsIHasDomainEvents_SoInfraCanQueryWithoutGenerics()
    {
        var agg = new TestAggregate();

        agg.Should().BeAssignableTo<IHasDomainEvents>();
    }
}
