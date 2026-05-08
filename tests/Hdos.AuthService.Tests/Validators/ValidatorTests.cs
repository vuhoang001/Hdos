using FluentAssertions;
using FluentValidation.TestHelper;
using Hdos.AuthService.Application.Features.Login;
using Hdos.AuthService.Application.Features.Register;
using Xunit;

namespace Hdos.AuthService.Tests.Validators;

public sealed class RegisterUserCommandValidatorTests
{
    private readonly RegisterUserCommandValidator _v = new();

    [Theory]
    [InlineData("", "Alice", "secret123")]
    [InlineData("not-email", "Alice", "secret123")]
    public void Email_Invalid(string email, string name, string pw)
    {
        var r = _v.TestValidate(new RegisterUserCommand(email, name, pw));
        r.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [InlineData("a@b.io", "", "secret123")]
    public void FullName_Required(string email, string name, string pw)
    {
        _v.TestValidate(new RegisterUserCommand(email, name, pw))
            .ShouldHaveValidationErrorFor(x => x.FullName);
    }

    [Theory]
    [InlineData("a@b.io", "Alice", "")]
    [InlineData("a@b.io", "Alice", "12345")] // min 6
    public void Password_Constraints(string email, string name, string pw)
    {
        _v.TestValidate(new RegisterUserCommand(email, name, pw))
            .ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Valid_NoErrors()
    {
        _v.TestValidate(new RegisterUserCommand("alice@hdos.io", "Alice", "secret123"))
            .ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class LoginUserCommandValidatorTests
{
    private readonly LoginUserCommandValidator _v = new();

    [Theory]
    [InlineData("", "secret")]
    [InlineData("not-email", "secret")]
    public void Email_Invalid(string email, string password)
    {
        _v.TestValidate(new LoginUserCommand(email, password))
            .ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Password_Required()
    {
        _v.TestValidate(new LoginUserCommand("a@b.io", ""))
            .ShouldHaveValidationErrorFor(x => x.Password);
    }
}
