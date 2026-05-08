using FluentValidation.Results;

namespace Hdos.Common.Exceptions;

public sealed class ValidationException : Exception
{
    public ValidationException(IEnumerable<ValidationFailure> failures)
        : base("One or more validation errors occurred.")
    {
        Errors = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class NotFoundException : Exception
{
    public NotFoundException(string what) : base($"{what} was not found.") { }
}

public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
