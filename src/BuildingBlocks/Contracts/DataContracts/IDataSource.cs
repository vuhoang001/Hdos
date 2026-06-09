namespace Hdos.Contracts.DataContracts;

/// <summary>
/// Một nguồn cung cấp data cho contract. 1 contract có thể có nhiều source
/// (view DB, raw SQL, code-generated, external API, RabbitMQ event...).
/// Gateway pick source nào dựa vào <see cref="SourceCode"/> caller chỉ định,
/// hoặc fallback source đầu tiên đăng ký nếu caller không chỉ định.
/// </summary>
/// <typeparam name="TSchema">CLR type của 1 row — phải khớp <c>IDataContract.SchemaType</c>.</typeparam>
public interface IDataSource<TSchema> where TSchema : class
{
    /// <summary>Mã contract mà source này phục vụ. VD "finance.daily.row".</summary>
    string ContractCode { get; }

    /// <summary>Mã định danh source trong phạm vi 1 contract. VD "view", "sql", "demo", "rmq".</summary>
    string SourceCode { get; }

    /// <summary>Stream rows theo query. Implementor được phép trả empty stream, KHÔNG throw cho "no data".</summary>
    IAsyncEnumerable<TSchema> ReadAsync(DataContractQuery query, CancellationToken ct);
}
