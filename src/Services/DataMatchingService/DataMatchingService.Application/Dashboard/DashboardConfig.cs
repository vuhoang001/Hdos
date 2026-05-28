using System.Text.Json;

namespace Hdos.DataMatchingService.Application.Dashboard;

/// <summary>
/// Để thêm dashboard mới: kế thừa class này, override Code/Title/RecordTypes/BuildSections,
/// đăng ký trong DI: services.AddSingleton&lt;DashboardConfig, YourConfig&gt;()
/// </summary>
public abstract class DashboardConfig
{
    /// Dashboard code dùng trong URL: GET /dm/dashboards/{Code}
    public abstract string Code { get; }

    /// Tên hiển thị trong response
    public abstract string Title { get; }

    /// Danh sách RecordType cần fetch từ StagingRecord
    public abstract IReadOnlyList<string> RecordTypes { get; }

    /// Engine gọi hàm này sau khi đã fetch + parse toàn bộ data.
    /// key = recordType, value = danh sách rows đã parse từ CanonicalPayload
    public abstract List<DashboardSection> BuildSections(
        IReadOnlyDictionary<string, List<Dictionary<string, JsonElement>>> data,
        DateOnly reportDate);

    // --- Helpers dùng trong subclass ---

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
}
