using FluentAssertions;
using Hdos.SharedKernel;
using Xunit;

namespace Hdos.BuildingBlocks.Tests.SharedKernel;

public sealed class ValueObjectTests
{
    private sealed class Address : ValueObject
    {
        public string Street { get; }
        public string City { get; }
        public Address(string street, string city) { Street = street; City = city; }
        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Street;
            yield return City;
        }
    }

    private sealed class PostCode : ValueObject
    {
        public string Value { get; }
        public PostCode(string value) { Value = value; }
        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Value;
        }
    }

    [Fact]
    public void Equality_BasedOnComponents_NotReference()
    {
        var a = new Address("1 Main", "HCMC");
        var b = new Address("1 Main", "HCMC");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var a = new Address("1 Main", "HCMC");
        var b = new Address("2 Main", "HCMC");

        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentTypes_AreNotEqual()
    {
        ValueObject a = new Address("x", "y");
        ValueObject b = new PostCode("x");

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equality_NullSafe()
    {
        var a = new Address("x", "y");
        Address? b = null;

        (a == b).Should().BeFalse();
        (b == a).Should().BeFalse();
        (b == null).Should().BeTrue();
    }
}
