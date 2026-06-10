namespace Hdos.Contracts.DataContracts.FormPrefill;

/// <summary>
/// Shape output chuẩn để form pre-fill từ data contract. FE map từng <c>FieldName</c>
/// vào field tương ứng trong DynamicForm screen.
///
/// Multi-row support: 1 contract có thể trả nhiều row (1 row × 1 form). Consumer thường
/// chỉ pick 1 row đại diện (latest, top, aggregate) hoặc trả tất cả cho FE chọn.
///
/// <c>Single</c> populate khi caller truyền <c>?mode=single</c> — flat object để FE bind
/// thẳng vào expression <c>{{sources.ns.field}}</c> mà không cần lấy index 0 từ Rows.
/// </summary>
public sealed record FormPrefillResult(
    string                                       ContractCode,
    int                                          RowCount,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows)
{
    public IReadOnlyDictionary<string, object?>? Single { get; init; }
}
