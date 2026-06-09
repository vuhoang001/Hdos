using FluentAssertions;
using Hdos.Contracts.DataContracts;
using Xunit;

namespace Hdos.BuildingBlocks.Tests.Contracts.DataContracts;

public sealed class DataContractRegistryTests
{
    private sealed record FooRow(string Name);
    private sealed record BarRow(int Value);

    private sealed class FooContract : DataContract<FooRow>
    {
        public override string Code => "test.foo.row";
        public override string DisplayName => "Foo test contract";
    }

    private sealed class BarContract : DataContract<BarRow>
    {
        public override string Code => "test.bar.row";
        public override string DisplayName => "Bar test contract";
    }

    private sealed class DuplicateFooContract : DataContract<FooRow>
    {
        public override string Code => "test.foo.row";
        public override string DisplayName => "Duplicate Foo (intentional)";
    }

    [Fact]
    public void Empty_registry_has_no_codes()
    {
        var reg = new DataContractRegistry([]);
        reg.All.Should().BeEmpty();
        reg.Codes.Should().BeEmpty();
    }

    [Fact]
    public void Get_returns_contract_by_code_case_insensitive()
    {
        var reg = new DataContractRegistry([new FooContract(), new BarContract()]);

        reg.Get("test.foo.row").Should().NotBeNull();
        reg.Get("TEST.FOO.ROW").Should().NotBeNull();   // case insensitive
        reg.Get("nonexistent").Should().BeNull();
    }

    [Fact]
    public void Require_throws_for_unknown_contract()
    {
        var reg = new DataContractRegistry([new FooContract()]);

        var act = () => reg.Require("nonexistent");
        act.Should().Throw<DataContractNotFoundException>()
            .Which.ContractCode.Should().Be("nonexistent");
    }

    [Fact]
    public void Constructor_throws_on_duplicate_codes()
    {
        var act = () => new DataContractRegistry([new FooContract(), new DuplicateFooContract()]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*test.foo.row*");
    }

    [Fact]
    public void Contains_returns_true_for_registered_code()
    {
        var reg = new DataContractRegistry([new FooContract()]);

        reg.Contains("test.foo.row").Should().BeTrue();
        reg.Contains("test.bar.row").Should().BeFalse();
    }
}
