namespace Hdos.Contracts.DataContracts;

/// <summary>
/// Một consumer biến stream rows của contract thành output cụ thể (chart, export CSV, form prefill, notification...).
/// Consumer KHÔNG biết source — chỉ làm việc với stream <typeparamref name="TSchema"/>.
/// </summary>
/// <typeparam name="TSchema">CLR type của 1 row của contract.</typeparam>
/// <typeparam name="TOutput">Kiểu output sau khi consumer tổng hợp xong. VD SduiPage, byte[] CSV, void/Unit.</typeparam>
public interface IDataConsumer<TSchema, TOutput> where TSchema : class
{
    /// <summary>Mã contract mà consumer phục vụ.</summary>
    string ContractCode { get; }

    /// <summary>Mã định danh consumer trong phạm vi 1 contract. VD "chart", "export", "form-prefill".</summary>
    string ConsumerCode { get; }

    /// <summary>Consume stream → output. Implementor quyết định buffer all vs streaming aggregate.</summary>
    Task<TOutput> ConsumeAsync(IAsyncEnumerable<TSchema> stream, DataContractQuery query, CancellationToken ct);
}
