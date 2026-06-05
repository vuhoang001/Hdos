using FluentAssertions;
using Hdos.DynamicFormService.Application.Features.Providers.DeleteProvider;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace Hdos.DynamicFormService.Tests.Application;

public sealed class DeleteProviderCommandHandlerTests
{
    private readonly IProviderRepository    _providers  = Substitute.For<IProviderRepository>();
    private readonly IOperationRepository   _operations = Substitute.For<IOperationRepository>();
    private readonly IDynamicFormUnitOfWork _uow        = Substitute.For<IDynamicFormUnitOfWork>();

    private DeleteProviderCommandHandler CreateHandler() => new(_providers, _operations, _uow);

    [Fact]
    public async Task Handle_NotFound_ReturnsFailure()
    {
        _providers.GetByCodeAsync("dm", Arg.Any<CancellationToken>())
            .Returns((Provider?)null);

        var result = await CreateHandler().Handle(
            new DeleteProviderCommand("dm"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task Handle_HasOperations_ReturnsConflict()
    {
        var p = Provider.Create("dm", "DM", "/dm");
        _providers.GetByCodeAsync("dm", Arg.Any<CancellationToken>()).Returns(p);
        _operations.AnyByProviderAsync("dm", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(
            new DeleteProviderCommand("dm"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Conflict");
        _providers.DidNotReceive().Remove(Arg.Any<Provider>());
    }

    [Fact]
    public async Task Handle_NoOperations_RemovesAndSaves()
    {
        var p = Provider.Create("dm", "DM", "/dm");
        _providers.GetByCodeAsync("dm", Arg.Any<CancellationToken>()).Returns(p);
        _operations.AnyByProviderAsync("dm", Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateHandler().Handle(
            new DeleteProviderCommand("dm"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _providers.Received(1).Remove(p);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
