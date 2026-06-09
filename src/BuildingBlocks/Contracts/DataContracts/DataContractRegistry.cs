namespace Hdos.Contracts.DataContracts;

/// <summary>
/// Bảng tra cứu các <see cref="IDataContract"/> đã đăng ký DI.
/// Singleton — vì contract metadata (Code, SchemaType, DisplayName) là immutable trong app lifetime.
/// Source/Consumer/Validator thì RESOLVE QUA <see cref="DataContractGateway"/> (scoped) vì có thể là Scoped.
/// </summary>
public sealed class DataContractRegistry
{
    private readonly Dictionary<string, IDataContract> _contracts;

    public DataContractRegistry(IEnumerable<IDataContract> contracts)
    {
        var grouped = contracts
            .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var duplicates = grouped.Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException(
                $"Duplicate data contract codes registered: {string.Join(", ", duplicates)}. " +
                "Each contract must have a unique Code.");

        _contracts = grouped.ToDictionary(
            g => g.Key,
            g => g.Single(),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IDataContract> All => _contracts.Values;

    public IReadOnlyCollection<string> Codes => _contracts.Keys;

    public IDataContract? Get(string contractCode) =>
        _contracts.TryGetValue(contractCode, out var c) ? c : null;

    public IDataContract Require(string contractCode) =>
        Get(contractCode) ?? throw new DataContractNotFoundException(contractCode);

    public bool Contains(string contractCode) =>
        _contracts.ContainsKey(contractCode);
}
