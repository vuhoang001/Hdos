using System.Text.RegularExpressions;
using Hdos.SharedKernel;

namespace Hdos.AuthService.Domain.ValueObjects;

public sealed class Email : ValueObject
{
    private static readonly Regex Pattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Value { get; }

    private Email(string value) => Value = value;

    public static Result<Email> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Failure<Email>(Error.Validation("Email is required"));
        var trimmed = raw.Trim().ToLowerInvariant();
        if (!Pattern.IsMatch(trimmed))
            return Result.Failure<Email>(Error.Validation("Email format is invalid"));
        return new Email(trimmed);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
