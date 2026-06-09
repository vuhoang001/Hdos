using System.Text.Json;

namespace Hdos.DataMatchingService.Application.Sdui;

/// <summary>
/// Để thêm page SDUI mới: kế thừa class này, override Code/RecordTypes/BuildPage,
/// đăng ký trong DI: services.AddSingleton&lt;SduiPageConfig, YourPageConfig&gt;()
/// </summary>
[Obsolete("Use DataContract layer (doc 53): define IDataContract<TSchema> + IDataSource<TSchema> 'staging' + IDataConsumer<TSchema, SduiPage>. Endpoint cũ /dm/pages/{code} vẫn hoạt động nhưng sẽ được xóa sau khi prod stable.", false)]
public abstract class SduiPageConfig
{
    /// Page code dùng trong URL: GET /dm/pages/{Code}
    public abstract string Code { get; }

    /// Danh sách RecordType cần fetch từ StagingRecord
    public abstract IReadOnlyList<string> RecordTypes { get; }

    /// Engine gọi hàm này sau khi đã fetch + parse toàn bộ data.
    /// key = recordType, value = danh sách rows đã parse từ CanonicalPayload
    public abstract SduiPage BuildPage(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate);

    // --- Helpers ---

    protected static string? Str(Dictionary<string, JsonElement> row, string key) =>
        row.TryGetValue(key, out var v) ? v.ToString() : null;

    protected static int Int(Dictionary<string, JsonElement> row, string key)
    {
        if (!row.TryGetValue(key, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
    }

    protected static decimal Dec(Dictionary<string, JsonElement> row, string key)
    {
        if (!row.TryGetValue(key, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : 0;
    }

    protected static DateOnly? Date(Dictionary<string, JsonElement> row, string key) =>
        DateOnly.TryParse(Str(row, key), out var d) ? d : null;

    protected static bool Bool(Dictionary<string, JsonElement> row, string key) =>
        row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.True;
}
