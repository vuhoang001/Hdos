using System.Runtime.CompilerServices;
using FluentAssertions;
using Hdos.Contracts.DataContracts;
using Hdos.Contracts.DataContracts.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hdos.BuildingBlocks.Tests.Contracts.DataContracts;

public sealed class DataContractGatewayTests
{
    private sealed record FooRow(string Name, int Score);

    private sealed class FooContract : DataContract<FooRow>
    {
        public override string Code => "test.foo.row";
        public override string DisplayName => "Foo test contract";
    }

    private sealed class FooMemorySource : IDataSource<FooRow>
    {
        public string ContractCode => "test.foo.row";
        public string SourceCode   => "memory";

        public async IAsyncEnumerable<FooRow> ReadAsync(
            DataContractQuery query, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield return new FooRow("alpha", 1);
            yield return new FooRow("beta",  2);
        }
    }

    private sealed class FooDemoSource : IDataSource<FooRow>
    {
        public string ContractCode => "test.foo.row";
        public string SourceCode   => "demo";

        public async IAsyncEnumerable<FooRow> ReadAsync(
            DataContractQuery query, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield return new FooRow("demo-only", 99);
        }
    }

    private sealed class FooSumConsumer : IDataConsumer<FooRow, int>
    {
        public string ContractCode => "test.foo.row";
        public string ConsumerCode => "sum";

        public async Task<int> ConsumeAsync(
            IAsyncEnumerable<FooRow> stream, DataContractQuery query, CancellationToken ct)
        {
            var sum = 0;
            await foreach (var r in stream.WithCancellation(ct)) sum += r.Score;
            return sum;
        }
    }

    private sealed class FooRejectValidator : IDataContractValidator<FooRow>
    {
        public string ContractCode => "test.foo.row";

        public ValueTask<DataContractValidationResult> ValidateAsync(FooRow row, CancellationToken ct) =>
            ValueTask.FromResult(row.Score < 0
                ? DataContractValidationResult.Invalid("Score must be >= 0")
                : DataContractValidationResult.Valid);
    }

    private sealed record BarRow(string Name);

    private static DataContractGateway BuildGateway(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services
            .AddDataContracts()
            .AddDataContract<FooContract>()
            .AddDataSource<FooRow, FooMemorySource>()
            .AddDataSource<FooRow, FooDemoSource>()
            .AddDataConsumer<FooRow, int, FooSumConsumer>()
            .AddDataContractValidator<FooRow, FooRejectValidator>();
        extra?.Invoke(services);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<DataContractGateway>();
    }

    [Fact]
    public async Task ReadAsync_default_picks_first_registered_source()
    {
        var gw = BuildGateway();
        var rows = new List<FooRow>();
        await foreach (var r in gw.ReadAsync<FooRow>("test.foo.row", sourceCode: null, DataContractQuery.Empty, default))
            rows.Add(r);

        rows.Should().HaveCount(2);
        rows[0].Name.Should().Be("alpha");
    }

    [Fact]
    public async Task ReadAsync_specific_source_picked_by_code()
    {
        var gw = BuildGateway();
        var rows = new List<FooRow>();
        await foreach (var r in gw.ReadAsync<FooRow>("test.foo.row", sourceCode: "demo", DataContractQuery.Empty, default))
            rows.Add(r);

        rows.Should().HaveCount(1);
        rows[0].Name.Should().Be("demo-only");
    }

    [Fact]
    public async Task ReadAsync_throws_for_unknown_source_code()
    {
        var gw = BuildGateway();

        var act = async () =>
        {
            await foreach (var _ in gw.ReadAsync<FooRow>("test.foo.row", sourceCode: "nonexistent", DataContractQuery.Empty, default))
            { }
        };
        await act.Should().ThrowAsync<DataSourceNotFoundException>();
    }

    [Fact]
    public async Task ReadAsync_throws_for_schema_mismatch()
    {
        var gw = BuildGateway();

        var act = async () =>
        {
            await foreach (var _ in gw.ReadAsync<BarRow>("test.foo.row", sourceCode: null, DataContractQuery.Empty, default))
            { }
        };
        await act.Should().ThrowAsync<DataContractSchemaMismatchException>();
    }

    [Fact]
    public async Task ConsumeAsync_returns_consumer_output()
    {
        var gw = BuildGateway();
        var sum = await gw.ConsumeAsync<FooRow, int>("test.foo.row", "sum", "memory", DataContractQuery.Empty, default);
        sum.Should().Be(3);  // alpha(1) + beta(2)
    }

    [Fact]
    public async Task ConsumeAsync_throws_for_unknown_consumer()
    {
        var gw = BuildGateway();

        var act = async () => await gw.ConsumeAsync<FooRow, int>(
            "test.foo.row", "nonexistent-consumer", "memory", DataContractQuery.Empty, default);

        await act.Should().ThrowAsync<DataConsumerNotFoundException>();
    }

    [Fact]
    public async Task ValidateAsync_returns_valid_when_no_errors()
    {
        var gw = BuildGateway();
        var result = await gw.ValidateAsync("test.foo.row", new FooRow("ok", 5), default);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_returns_invalid_for_bad_data()
    {
        var gw = BuildGateway();
        var result = await gw.ValidateAsync("test.foo.row", new FooRow("bad", -1), default);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Score"));
    }

    [Fact]
    public async Task ValidateAsync_returns_valid_when_no_validator_registered()
    {
        var services = new ServiceCollection();
        services.AddDataContracts()
                .AddDataContract<FooContract>();
        var gw = services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetRequiredService<DataContractGateway>();

        var result = await gw.ValidateAsync("test.foo.row", new FooRow("ok", -100), default);
        result.IsValid.Should().BeTrue();  // No validator → assume valid
    }
}
