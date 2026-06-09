namespace Hdos.Contracts.DataContracts;

/// <summary>
/// Tham số truy vấn truyền vào source/consumer. Cố tình giữ dạng string filters
/// để source nào cũng parse được — date, dept, status,... đều là string.
/// </summary>
public sealed record DataContractQuery(
    IReadOnlyDictionary<string, string?> Filters,
    int? Limit = null)
{
    public static DataContractQuery Empty { get; } =
        new(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), null);

    public string? Get(string key) =>
        Filters.TryGetValue(key, out var v) ? v : null;

    public bool TryGet(string key, out string? value)
    {
        var has = Filters.TryGetValue(key, out var v);
        value = v;
        return has;
    }

    public int? GetInt(string key) =>
        int.TryParse(Get(key), out var i) ? i : null;

    public DateOnly? GetDate(string key) =>
        DateOnly.TryParse(Get(key), out var d) ? d : null;

    public bool GetBool(string key, bool fallback = false) =>
        bool.TryParse(Get(key), out var b) ? b : fallback;

    public static DataContractQuery From(IEnumerable<KeyValuePair<string, string?>> pairs, int? limit = null)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in pairs) dict[kvp.Key] = kvp.Value;
        return new DataContractQuery(dict, limit);
    }
}
