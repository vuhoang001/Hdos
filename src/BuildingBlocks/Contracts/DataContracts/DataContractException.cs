namespace Hdos.Contracts.DataContracts;

public class DataContractException : Exception
{
    public string ContractCode { get; }

    public DataContractException(string contractCode, string message)
        : base(message)
    {
        ContractCode = contractCode;
    }

    public DataContractException(string contractCode, string message, Exception inner)
        : base(message, inner)
    {
        ContractCode = contractCode;
    }
}

public sealed class DataContractNotFoundException : DataContractException
{
    public DataContractNotFoundException(string contractCode)
        : base(contractCode, $"Data contract '{contractCode}' is not registered.") { }
}

public sealed class DataSourceNotFoundException : DataContractException
{
    public string SourceCode { get; }

    public DataSourceNotFoundException(string contractCode, string sourceCode)
        : base(contractCode, $"Data source '{sourceCode}' for contract '{contractCode}' is not registered.")
    {
        SourceCode = sourceCode;
    }
}

public sealed class DataConsumerNotFoundException : DataContractException
{
    public string ConsumerCode { get; }

    public DataConsumerNotFoundException(string contractCode, string consumerCode)
        : base(contractCode, $"Data consumer '{consumerCode}' for contract '{contractCode}' is not registered.")
    {
        ConsumerCode = consumerCode;
    }
}

public sealed class DataContractSchemaMismatchException : DataContractException
{
    public DataContractSchemaMismatchException(string contractCode, Type expected, Type actual)
        : base(contractCode,
              $"Schema mismatch for contract '{contractCode}': expected {expected.FullName}, got {actual.FullName}.") { }
}

public sealed class DataContractValidationException : DataContractException
{
    public IReadOnlyList<string> ValidationErrors { get; }

    public DataContractValidationException(string contractCode, IReadOnlyList<string> errors)
        : base(contractCode, $"Validation failed for contract '{contractCode}': {string.Join("; ", errors)}")
    {
        ValidationErrors = errors;
    }
}
