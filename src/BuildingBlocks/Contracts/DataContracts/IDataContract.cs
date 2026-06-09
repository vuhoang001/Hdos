namespace Hdos.Contracts.DataContracts;

/// <summary>
/// Một data contract = khai báo schema (type) + code duy nhất.
/// Là tầng abstraction cao nhất của "phễu" data: ai có data đúng schema này → push qua gateway,
/// ai cần data này → đọc qua gateway, không cần biết source là view DB, raw SQL, code, API hay event.
/// </summary>
public interface IDataContract
{
    /// <summary>Mã định danh duy nhất toàn hệ thống. Convention: namespace.entity.kind. VD "finance.daily.row".</summary>
    string Code { get; }

    /// <summary>CLR type của 1 row trong contract — thường là <c>record</c> immutable.</summary>
    Type SchemaType { get; }

    /// <summary>Mô tả ngắn để hiển thị UI / metadata endpoint.</summary>
    string DisplayName { get; }
}

/// <summary>
/// Base class typed cho contract — implementor chỉ cần override Code + DisplayName.
/// </summary>
public abstract class DataContract<TSchema> : IDataContract where TSchema : class
{
    public abstract string Code { get; }
    public abstract string DisplayName { get; }
    public Type SchemaType => typeof(TSchema);
}
