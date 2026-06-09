using Microsoft.Extensions.DependencyInjection;

namespace Hdos.Contracts.DataContracts;

/// <summary>
/// "Phễu" trung tâm. Tất cả việc đọc/validate/consume data của contract đều qua gateway này.
/// Caller chỉ cần biết: <c>contractCode</c> (mã contract) + optional <c>sourceCode</c> (chọn source nào).
/// Không cần biết source là view DB, raw SQL, code, API hay event.
///
/// Lifetime: Scoped — vì nó resolve <see cref="IDataSource{TSchema}"/> / Consumer / Validator
/// từ IServiceProvider của request scope (các source thường scoped: dùng DbContext, HttpClient,...).
/// </summary>
public sealed class DataContractGateway
{
    private readonly DataContractRegistry _registry;
    private readonly IServiceProvider _services;

    public DataContractGateway(DataContractRegistry registry, IServiceProvider services)
    {
        _registry = registry;
        _services = services;
    }

    public IReadOnlyCollection<IDataContract> AvailableContracts => _registry.All;

    public IDataContract Require(string contractCode) => _registry.Require(contractCode);

    // ─────────────────────────────────────────────────────────────────────
    // READ — pull stream rows từ source
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Đọc stream rows từ contract. Nếu <paramref name="sourceCode"/> null → dùng source đầu tiên đăng ký.
    /// </summary>
    public IAsyncEnumerable<TSchema> ReadAsync<TSchema>(
        string contractCode,
        string? sourceCode,
        DataContractQuery query,
        CancellationToken ct) where TSchema : class
    {
        EnsureSchemaType<TSchema>(contractCode);

        var sources = ListSourcesInternal<TSchema>(contractCode);
        if (sources.Count == 0)
            throw new DataSourceNotFoundException(contractCode, sourceCode ?? "*");

        var picked = sourceCode is null
            ? sources[0]
            : sources.FirstOrDefault(s => Eq(s.SourceCode, sourceCode))
              ?? throw new DataSourceNotFoundException(contractCode, sourceCode);

        return picked.ReadAsync(query, ct);
    }

    public IReadOnlyList<IDataSource<TSchema>> ListSources<TSchema>(string contractCode) where TSchema : class
    {
        EnsureSchemaType<TSchema>(contractCode);
        return ListSourcesInternal<TSchema>(contractCode);
    }

    // ─────────────────────────────────────────────────────────────────────
    // VALIDATE — schema-level validation 1 row
    // ─────────────────────────────────────────────────────────────────────

    public async Task<DataContractValidationResult> ValidateAsync<TSchema>(
        string contractCode,
        TSchema row,
        CancellationToken ct) where TSchema : class
    {
        EnsureSchemaType<TSchema>(contractCode);

        var validator = _services.GetServices<IDataContractValidator<TSchema>>()
            .FirstOrDefault(v => Eq(v.ContractCode, contractCode));

        return validator is null
            ? DataContractValidationResult.Valid
            : await validator.ValidateAsync(row, ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // CONSUME — read + pipe stream qua consumer → output cụ thể
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// End-to-end: chọn source → stream rows → consume → trả output.
    /// VD chart: <c>Gateway.ConsumeAsync&lt;FinanceDailyRow, SduiPage&gt;("finance.daily.row", "chart", "sql", query, ct)</c>.
    /// </summary>
    public async Task<TOutput> ConsumeAsync<TSchema, TOutput>(
        string contractCode,
        string consumerCode,
        string? sourceCode,
        DataContractQuery query,
        CancellationToken ct) where TSchema : class
    {
        EnsureSchemaType<TSchema>(contractCode);

        var consumer = _services.GetServices<IDataConsumer<TSchema, TOutput>>()
            .FirstOrDefault(c => Eq(c.ContractCode, contractCode) && Eq(c.ConsumerCode, consumerCode))
            ?? throw new DataConsumerNotFoundException(contractCode, consumerCode);

        var stream = ReadAsync<TSchema>(contractCode, sourceCode, query, ct);
        return await consumer.ConsumeAsync(stream, query, ct);
    }

    public IReadOnlyList<IDataConsumer<TSchema, TOutput>> ListConsumers<TSchema, TOutput>(string contractCode)
        where TSchema : class
    {
        EnsureSchemaType<TSchema>(contractCode);
        return _services.GetServices<IDataConsumer<TSchema, TOutput>>()
            .Where(c => Eq(c.ContractCode, contractCode))
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private IReadOnlyList<IDataSource<TSchema>> ListSourcesInternal<TSchema>(string contractCode)
        where TSchema : class =>
        _services.GetServices<IDataSource<TSchema>>()
            .Where(s => Eq(s.ContractCode, contractCode))
            .ToList();

    private void EnsureSchemaType<TSchema>(string contractCode)
    {
        var contract = _registry.Require(contractCode);
        if (contract.SchemaType != typeof(TSchema))
            throw new DataContractSchemaMismatchException(contractCode, contract.SchemaType, typeof(TSchema));
    }

    private static bool Eq(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
