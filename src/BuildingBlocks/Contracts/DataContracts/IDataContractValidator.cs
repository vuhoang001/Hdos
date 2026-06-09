namespace Hdos.Contracts.DataContracts;

/// <summary>
/// Validator schema-level cho 1 contract. Optional — nếu không đăng ký, gateway coi mọi row đều valid.
/// Implementor có thể wrap FluentValidation hoặc viết logic tùy ý, miễn return <see cref="DataContractValidationResult"/>.
/// </summary>
public interface IDataContractValidator<TSchema> where TSchema : class
{
    string ContractCode { get; }
    ValueTask<DataContractValidationResult> ValidateAsync(TSchema row, CancellationToken ct);
}

public sealed record DataContractValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static DataContractValidationResult Valid { get; } = new(true, []);
    public static DataContractValidationResult Invalid(params string[] errors) => new(false, errors);
    public static DataContractValidationResult FromMessages(IEnumerable<string> errors)
    {
        var list = errors.ToList();
        return list.Count == 0 ? Valid : new(false, list);
    }
}
