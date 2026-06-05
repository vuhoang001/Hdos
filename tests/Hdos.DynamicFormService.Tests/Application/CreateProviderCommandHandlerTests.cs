using FluentAssertions;
using Hdos.DynamicFormService.Application.Features.Providers.CreateProvider;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace Hdos.DynamicFormService.Tests.Application;

public sealed class CreateProviderCommandHandlerTests
{
    private readonly IProviderRepository    _providers = Substitute.For<IProviderRepository>();
    private readonly IDynamicFormUnitOfWork _uow       = Substitute.For<IDynamicFormUnitOfWork>();

    private CreateProviderCommandHandler CreateHandler() => new(_providers, _uow);

    [Fact]
    public async Task Handle_NewCode_CreatesProvider()
    {
        _providers.ExistsByCodeAsync("datamatch", Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateHandler().Handle(
            new CreateProviderCommand("DataMatch", "DM", "/dm"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Code.Should().Be("datamatch");
        result.Value.DisplayName.Should().Be("DM");
        result.Value.BaseUrl.Should().Be("/dm");
        result.Value.OperationCount.Should().Be(0);

        await _providers.Received(1).AddAsync(Arg.Any<Provider>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsConflict()
    {
        _providers.ExistsByCodeAsync("datamatch", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(
            new CreateProviderCommand("datamatch", "DM", "/dm"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Conflict");

        await _providers.DidNotReceive().AddAsync(Arg.Any<Provider>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
