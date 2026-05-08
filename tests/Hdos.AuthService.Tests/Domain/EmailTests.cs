using FluentAssertions;
using Hdos.AuthService.Domain.ValueObjects;
using Xunit;

namespace Hdos.AuthService.Tests.Domain;

public sealed class EmailTests
{
    [Theory]
    [InlineData("alice@hdos.io")]
    [InlineData("ALICE@HDOS.IO")]
    [InlineData("  alice@hdos.io  ")]
    [InlineData("a.b+tag@x.co")]
    public void Create_ValidInput_NormalizesAndSucceeds(string raw)
    {
        var result = Email.Create(raw);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(raw.Trim().ToLowerInvariant());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrEmpty_FailsWithValidation(string? raw)
    {
        var result = Email.Create(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("required");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@nope.com")]
    [InlineData("nope@")]
    [InlineData("two@@signs.io")]
    [InlineData("no-tld@host")]
    public void Create_BadFormat_FailsWithValidation(string raw)
    {
        var result = Email.Create(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("format");
    }

    [Fact]
    public void Equality_BasedOnNormalizedValue()
    {
        var a = Email.Create("alice@hdos.io").Value;
        var b = Email.Create("ALICE@hdos.IO").Value;

        a.Should().Be(b);
    }
}
