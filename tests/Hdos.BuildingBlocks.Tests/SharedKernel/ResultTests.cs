using FluentAssertions;
using Hdos.SharedKernel;
using Xunit;

namespace Hdos.BuildingBlocks.Tests.SharedKernel;

public sealed class ResultTests
{
    [Fact]
    public void Success_NonGeneric_HasNoError()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_NonGeneric_CarriesError()
    {
        var error = Error.Validation("bad");
        var result = Result.Failure(error);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Success_Generic_ExposesValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Failure_Generic_ThrowsOnValueAccess()
    {
        var result = Result.Failure<int>(Error.NotFound("Thing"));

        var act = () => _ = result.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ImplicitConversion_FromValue_ProducesSuccess()
    {
        Result<string> result = "hello";

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void Constructor_RejectsContradictoryStates()
    {
        var act1 = () => Result.Failure(Error.None);
        act1.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("NotFound")]
    [InlineData("Validation")]
    [InlineData("Conflict")]
    [InlineData("Unauthorized")]
    public void ErrorFactories_ProduceCanonicalCodes(string expectedCode)
    {
        Error error = expectedCode switch
        {
            "NotFound"     => Error.NotFound("X"),
            "Validation"   => Error.Validation("x"),
            "Conflict"     => Error.Conflict("x"),
            "Unauthorized" => Error.Unauthorized(),
            _              => throw new InvalidOperationException()
        };

        error.Code.Should().Be(expectedCode);
        error.Message.Should().NotBeNullOrEmpty();
    }
}
