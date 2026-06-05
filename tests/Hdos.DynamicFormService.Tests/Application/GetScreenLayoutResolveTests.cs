using System.Text.Json;
using FluentAssertions;
using Hdos.DynamicFormService.Application.Features.Screens.GetScreenLayout;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.DynamicFormService.Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace Hdos.DynamicFormService.Tests.Application;

// Tập trung vào resolve pipeline của DataSource (managed vs legacy).
public sealed class GetScreenLayoutResolveTests
{
    private readonly IFormScreenRepository   _screens    = Substitute.For<IFormScreenRepository>();
    private readonly IFormTemplateRepository _templates  = Substitute.For<IFormTemplateRepository>();
    private readonly IProviderRepository     _providers  = Substitute.For<IProviderRepository>();
    private readonly IOperationRepository    _operations = Substitute.For<IOperationRepository>();

    private GetScreenLayoutQueryHandler CreateHandler() =>
        new(_screens, _templates, _providers, _operations);

    [Fact]
    public async Task Handle_LegacyDataSource_PassesThroughWithoutResolving()
    {
        var legacy = new DataSource(
            Namespace:      "benhnhan",
            ServiceId:      "datamatch",
            ResourcePath:   "/dm/records?value={maBN}",
            RequiredParams: new() { "maBN" },
            SchemaPath:     "/dm/sources/his-01/benh-nhan/schema");

        var screen = BuildScreen(new[] { legacy });
        _screens.GetWithTabsAndWidgetsAsync("kham-benh", "tiep-nhan", Arg.Any<CancellationToken>())
            .Returns(screen);

        var result = await CreateHandler().Handle(
            new GetScreenLayoutQuery("kham-benh", "tiep-nhan"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var ds = result.Value.DataSources.Should().ContainSingle().Subject;
        ds.Namespace.Should().Be("benhnhan");
        ds.ServiceId.Should().Be("datamatch");
        ds.ResourcePath.Should().Be("/dm/records?value={maBN}");
        ds.SchemaPath.Should().Be("/dm/sources/his-01/benh-nhan/schema");
        ds.BaseUrl.Should().BeNull();
        ds.Kind.Should().BeNull();
        ds.OperationId.Should().BeNull();

        await _providers.DidNotReceive().GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _operations.DidNotReceive().GetByKeyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ManagedDataSource_ResolvesProviderAndOperation()
    {
        var managed = new DataSource(
            Namespace:      "benhnhan",
            ServiceId:      null,
            ResourcePath:   null,
            RequiredParams: new(),     // FE chưa override → handler lấy từ Operation
            OperationId:    "datamatch::patient-by-mabn");

        var screen   = BuildScreen(new[] { managed });
        var provider = Provider.Create("datamatch", "Data Matching", "/dm");
        var op       = Operation.Create("datamatch", "patient-by-mabn", "Patient by MaBN",
                            "/records?value={maBN}", "/sources/his-01/benh-nhan/schema",
                            new[] { "maBN" }, OperationKind.Single);

        _screens.GetWithTabsAndWidgetsAsync("kham-benh", "tiep-nhan", Arg.Any<CancellationToken>())
            .Returns(screen);
        _providers.GetByCodeAsync("datamatch", Arg.Any<CancellationToken>()).Returns(provider);
        _operations.GetByKeyAsync("datamatch", "patient-by-mabn", Arg.Any<CancellationToken>()).Returns(op);

        var result = await CreateHandler().Handle(
            new GetScreenLayoutQuery("kham-benh", "tiep-nhan"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var ds = result.Value.DataSources.Should().ContainSingle().Subject;
        ds.Namespace.Should().Be("benhnhan");
        ds.ServiceId.Should().Be("datamatch");
        ds.BaseUrl.Should().Be("/dm");
        ds.ResourcePath.Should().Be("/records?value={maBN}");
        ds.SchemaPath.Should().Be("/sources/his-01/benh-nhan/schema");
        ds.RequiredParams.Should().Equal("maBN");
        ds.Kind.Should().Be("Single");
        ds.OperationId.Should().Be("datamatch::patient-by-mabn");
    }

    [Fact]
    public async Task Handle_ManagedDataSource_OperationMissing_FallsBackToStoredFields()
    {
        var managed = new DataSource(
            Namespace:      "benhnhan",
            ServiceId:      null,
            ResourcePath:   null,
            RequiredParams: new(),
            OperationId:    "datamatch::was-deleted");

        var screen = BuildScreen(new[] { managed });
        _screens.GetWithTabsAndWidgetsAsync("kham-benh", "tiep-nhan", Arg.Any<CancellationToken>())
            .Returns(screen);
        _providers.GetByCodeAsync("datamatch", Arg.Any<CancellationToken>())
            .Returns(Provider.Create("datamatch", "DM", "/dm"));
        _operations.GetByKeyAsync("datamatch", "was-deleted", Arg.Any<CancellationToken>())
            .Returns((Operation?)null);

        var result = await CreateHandler().Handle(
            new GetScreenLayoutQuery("kham-benh", "tiep-nhan"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var ds = result.Value.DataSources.Should().ContainSingle().Subject;
        ds.OperationId.Should().Be("datamatch::was-deleted");
        ds.BaseUrl.Should().BeNull("không resolve được khi Operation đã bị xóa");
        ds.ResourcePath.Should().BeNull();
    }

    // FormScreen entity dùng internal constructor — dùng reflection để khởi tạo trong test.
    // Cách này tránh phải mở rộng public surface của Domain chỉ vì test.
    private static FormScreen BuildScreen(IEnumerable<DataSource> dataSources)
    {
        var screen = FormScreen.Create(
            moduleId: Guid.NewGuid(),
            moduleCode: "kham-benh",
            code: "tiep-nhan",
            title: "Tiếp nhận",
            description: null);

        screen.SetDataSources(dataSources.ToList());
        return screen;
    }
}
