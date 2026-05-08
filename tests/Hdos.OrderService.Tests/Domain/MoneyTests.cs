using FluentAssertions;
using Hdos.OrderService.Domain.ValueObjects;
using Xunit;

namespace Hdos.OrderService.Tests.Domain;

public sealed class MoneyTests
{
    [Fact]
    public void Of_PositiveAmount_NormalizesCurrencyToUpper()
    {
        var m = Money.Of(10m, "usd");
        m.Amount.Should().Be(10m);
        m.Currency.Should().Be("USD");
    }

    [Fact]
    public void Of_NegativeAmount_Throws()
    {
        var act = () => Money.Of(-1m, "USD");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_BlankCurrency_Throws(string c)
    {
        var act = () => Money.Of(1m, c);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Add_SameCurrency_Sums()
    {
        var sum = Money.Of(5m, "USD").Add(Money.Of(3m, "USD"));

        sum.Amount.Should().Be(8m);
        sum.Currency.Should().Be("USD");
    }

    [Fact]
    public void Add_DifferentCurrency_Throws()
    {
        var act = () => Money.Of(5m, "USD").Add(Money.Of(3m, "VND"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*mismatch*");
    }

    [Fact]
    public void Equality_BasedOnAmountAndCurrency()
    {
        Money.Of(10m, "USD").Should().Be(Money.Of(10m, "usd"));
        Money.Of(10m, "USD").Should().NotBe(Money.Of(10m, "VND"));
    }
}
