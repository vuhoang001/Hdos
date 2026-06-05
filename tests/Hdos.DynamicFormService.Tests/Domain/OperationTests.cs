using FluentAssertions;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Xunit;

namespace Hdos.DynamicFormService.Tests.Domain;

public sealed class OperationTests
{
    [Fact]
    public void Create_NormalizesCodes_AndSetsActiveStatus()
    {
        var op = Operation.Create(
            "DataMatch", "Patient-By-MaBN", "Tìm BN",
            "/records?value={maBN}", "/sources/his-01/benh-nhan/schema",
            new[] { "maBN" }, OperationKind.Single);

        op.Id.Should().NotBeEmpty();
        op.ProviderCode.Should().Be("datamatch");
        op.OperationKey.Should().Be("patient-by-mabn");
        op.DisplayName.Should().Be("Tìm BN");
        op.Pattern.Should().Be("/records?value={maBN}");
        op.SchemaPath.Should().Be("/sources/his-01/benh-nhan/schema");
        op.GetRequiredParams().Should().Equal("maBN");
        op.Kind.Should().Be(OperationKind.Single);
        op.Status.Should().Be(OperationStatus.Active);
    }

    [Fact]
    public void Create_EmptyOrNullRequiredParams_StoresEmptyList()
    {
        var op = Operation.Create("dm", "list-all", "List", "/records", null,
            Enumerable.Empty<string>(), OperationKind.List);

        op.GetRequiredParams().Should().BeEmpty();
        op.SchemaPath.Should().BeNull();
    }

    [Fact]
    public void Create_DeduplicatesRequiredParams_PreservingOrder()
    {
        var op = Operation.Create("dm", "x", "X", "/x?a={a}&b={b}", null,
            new[] { "a", "b", "a", "  ", "b" }, OperationKind.Single);

        op.GetRequiredParams().Should().Equal("a", "b");
    }

    [Theory]
    [InlineData(null, "k", "d", "/p")]
    [InlineData("dm", null, "d", "/p")]
    [InlineData("dm", "k", null, "/p")]
    [InlineData("dm", "k", "d", null)]
    public void Create_BlankRequired_Throws(string? providerCode, string? key, string? displayName, string? pattern) =>
        FluentActions.Invoking(() => Operation.Create(
                providerCode!, key!, displayName!, pattern!, null,
                Array.Empty<string>(), OperationKind.Single))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void GetCombinedRef_ReturnsProviderCodeAndOperationKeySeparatedByDoubleColon()
    {
        var op = Operation.Create("dm", "patient-by-mabn", "X", "/x", null,
            Array.Empty<string>(), OperationKind.Single);

        op.GetCombinedRef().Should().Be("dm::patient-by-mabn");
    }

    [Fact]
    public void Update_AppliesValuesAndSetsUpdatedAt()
    {
        var op = Operation.Create("dm", "k", "Old", "/old", null,
            new[] { "a" }, OperationKind.Single);

        op.Update("New", "/new?x={x}", "/schema",
            new[] { "x" }, OperationKind.List);

        op.DisplayName.Should().Be("New");
        op.Pattern.Should().Be("/new?x={x}");
        op.SchemaPath.Should().Be("/schema");
        op.GetRequiredParams().Should().Equal("x");
        op.Kind.Should().Be(OperationKind.List);
        op.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void DeactivateAndActivate_ToggleStatus()
    {
        var op = Operation.Create("dm", "k", "X", "/x", null,
            Array.Empty<string>(), OperationKind.Single);

        op.Deactivate();
        op.Status.Should().Be(OperationStatus.Inactive);

        op.Activate();
        op.Status.Should().Be(OperationStatus.Active);
    }
}
