using FluentAssertions;
using Hdos.Common.Persistence;
using Hdos.SharedKernel;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hdos.BuildingBlocks.Tests.Persistence;

public sealed class LoggingDomainEventHandlerTests
{
    // Type must be public/internal-accessible — Castle DynamicProxy can't proxy
    // ILogger<T> when T is private.
    public sealed record FooEvent : DomainEvent;

    [Fact]
    public async Task Handle_DoesNotThrow_AndCompletesSynchronously()
    {
        var handler = new LoggingDomainEventHandler<FooEvent>(
            NullLogger<LoggingDomainEventHandler<FooEvent>>.Instance);

        var task = handler.Handle(new FooEvent(), CancellationToken.None);

        task.IsCompletedSuccessfully.Should().BeTrue("logging handler is purely synchronous");
        await task;
    }
}
